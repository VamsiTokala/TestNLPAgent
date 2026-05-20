using System.Text;
using System.Text.Json;
using ElimsInsightAssistant.Api.Models;
using Microsoft.Extensions.Logging;
using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Types;
using OpenAI.Chat;

namespace ElimsInsightAssistant.Api.Services;

// ─── Result type ─────────────────────────────────────────────────────────────
// Separates two distinct failure modes so the controller can respond correctly:
//   IsServerError = false → query was understood but not supported (200 UnsupportedQuery)
//   IsServerError = true  → transient failure: network, provider, parse error (503)

public record PlanGeneratorResult(
    string Markdown,
    ExecutionPlan? Plan,
    string? Error,
    bool IsServerError = false);

// ─── Interface ────────────────────────────────────────────────────────────────

public interface IPlanGenerator
{
    string ProviderName { get; }
    Task<PlanGeneratorResult> GenerateAsync(string query);
}

// ─── Shared prompt builder ────────────────────────────────────────────────────

internal static class PromptBuilder
{
    // Builds the REGISTERED SERVICE CONTRACTS block injected into every prompt.
    internal static string ServicesBlock(IReadOnlyList<ServiceContractEntry> contracts)
    {
        var sb = new StringBuilder();
        foreach (var c in contracts)
        {
            sb.AppendLine($"  {c.Name}{(c.IsRequired ? " [REQUIRED]" : " [OPTIONAL]")} → action: {c.Action}");
            sb.AppendLine($"    Purpose: {c.Purpose}");
            sb.AppendLine($"    Fields: {string.Join(", ", c.Fields)}");
        }
        return sb.ToString().TrimEnd();
    }

    // Builds the example operations array for the JSON schema embedded in the Gemini prompt.
    internal static string ExampleOperations(IReadOnlyList<ServiceContractEntry> contracts)
    {
        var ops = contracts.Select(c =>
        {
            var select = string.Join(", ", c.Fields.Select(f => $"\"{f}\""));
            var filter = c.Name == "corelabs-service"
                ? "[{\"field\":\"status\",\"op\":\"=\",\"value\":\"Completed\"}]"
                : "[]";
            return $"      {{ \"service\": \"{c.Name}\", \"action\": \"{c.Action}\", \"select\": [{select}], \"filters\": {filter}, \"reason\": \"why this service is needed for the query\" }}";
        });
        return string.Join(",\n", ops);
    }

    // The invariant sections shared across Gemini and OpenAI prompts.
    internal static string CoreInstructions(IReadOnlyList<ServiceContractEntry> contracts)
    {
        var servicesBlock = ServicesBlock(contracts);
        return $"""
REGISTERED SERVICE CONTRACTS (select only those needed to answer the query):
{servicesBlock}

ALLOWED FILTER OPERATORS: =, !=, >, >=, <, <=, in, between, is null, is not null
ALLOWED AGGREGATE FUNCTIONS: max, min, count, sum, avg
MAX ROWS LIMIT: 500

CLASSIFICATION RULES:
- "On Time": actualCompletionDate <= plannedCompletionDate (both dates present)
- "Delayed": actualCompletionDate > plannedCompletionDate (both dates present)
- "Indeterminate": plannedCompletionDate is null OR actualCompletionDate is null

THIS SYSTEM answers questions about study completion timeliness using the registered
service contracts above (studies, testps, protocols, samples, or any combination).

RULE: Set supported=true if the query involves any entity from the registered
contracts — studies, testps, protocols, samples — in any phrasing or combination.
This includes counting, listing, finding, summarising, correlating, or filtering
across one or more contracts.

Set supported=false ONLY when the query has nothing to do with any registered
contract (e.g. weather, invoices, employee HR records, unrelated lab equipment).

SELECT OPERATIONS: include every contract whose data is needed to answer the query.
For queries spanning multiple entities (e.g. "studies with testp results"), include
all relevant contracts and explain each one's role in the reason field.

SET output.includeClassifications to the smallest set that fully answers the intent:
- "problems / not on time / at risk / delayed / overdue" → ["Delayed", "Indeterminate"]
- specifically only delayed → ["Delayed"]
- specifically only indeterminate / missing data → ["Indeterminate"]
- "on time / successful / met deadline" → ["On Time"]
- broad / count / all / overview / unclear / involves testps or other contracts → ["On Time", "Delayed", "Indeterminate"]

FOR EACH SELECTED OPERATION, provide a brief "reason" explaining why that service
is needed to answer this specific query.

If the query involves any registered contract (studies, testps, protocols, samples):
  - Set supported = true, reason = null
  - Set markdown to one short sentence describing the plan
  - Populate plan with version "1.0", intent "find_studies_not_completed_on_time",
    the required service operations with reasons, output.includeClassifications,
    and limits maxRows 500
  - IMPORTANT: intent, entities, and operations MUST all be fully populated.
    Never return blank intent or empty operations when supported=true.

If the query has nothing to do with any registered contract:
  - Set supported = false
  - Set reason to a one-sentence explanation
  - Set markdown = null and plan = null
""";
    }
}

// ─── Mock (no API key required — local dev and tests) ────────────────────────

public class MockPlanGenerator(IServiceRegistry registry, ILogger<MockPlanGenerator> logger) : IPlanGenerator
{
    public string ProviderName => "Mock";

    public Task<PlanGeneratorResult> GenerateAsync(string query)
    {
        var q = query.ToLowerInvariant();
        logger.LogInformation("Mock → query: {Query}", query);

        var classifications = ResolveClassifications(q);
        var contracts = registry.GetAll();

        var markdown = $"""
# Analysis Plan
Intent: Find studies not completed on time.

## Contract mapping
{string.Join("\n", contracts.Select(c => $"- {c.DisplayName}: {c.Purpose}"))}

## Steps
1. Fetch studies from Study Service.
2. Fetch completed TestPs from CoreLabs Service.
3. Correlate Study and TestP records using studyId.
4. Group TestPs by studyId.
5. Derive actual study completion as the maximum TestP completedAt timestamp.
6. Compare actual completion date with planned completion date.
7. Classify each study as On Time, Delayed, or Indeterminate.
8. Return {string.Join(" and ", classifications)} studies with supporting details.

## Execution mode
Read-only, deterministic, approved service contracts only.
""";

        var plan = new ExecutionPlan
        {
            Intent = "find_studies_not_completed_on_time",
            Entities = ["study", "testp"],
            Operations = contracts.Select(c => new PlanOperation(
                Service: c.Name,
                Action: c.Action,
                Select: [.. c.Fields],
                Filters: c.Name == "corelabs-service"
                    ? [new("status", "=", "Completed")]
                    : [],
                Reason: c.Name == "study-service"
                    ? "Required for study identity, planned completion dates, and legal entity filtering"
                    : "Required for actual completion timestamps derived from TestP records"
            )).ToList(),
            Output = new PlanOutput(classifications)
        };

        logger.LogInformation("Mock ← plan ready | classifications={Classifications} | services={Services}",
            string.Join(", ", classifications),
            string.Join(", ", contracts.Select(c => c.Name)));

        return Task.FromResult(new PlanGeneratorResult(markdown, plan, null));
    }

    private static List<string> ResolveClassifications(string q)
    {
        // Exact classification filter
        if (q.Contains("classification indeterminate")) return ["Indeterminate"];
        if (q.Contains("classification delayed"))       return ["Delayed"];
        if (q.Contains("classification on time"))       return ["On Time"];

        // Overview / all-studies queries
        bool isOverview = q.Contains("all studies") || q.Contains("show all") ||
                          q.Contains("list studies") || q.Contains("find studies") ||
                          q.Contains("how many") || q.Contains("study count") ||
                          q.Contains("total studies") || q.Contains("study status") ||
                          q.Contains("study summary") || q.Contains("breakdown") ||
                          q.Contains("overview") || q.Contains("dashboard") ||
                          q.Contains("which studies");
        if (isOverview) return ["On Time", "Delayed", "Indeterminate"];

        // "not on time" negation — must check before plain "on time"
        bool notOnTime = q.Contains("not on time") || q.Contains("not completed on time") ||
                         q.Contains("haven't finished") || q.Contains("not finished");

        bool wantsDelayed = q.Contains("delayed") || q.Contains("overdue") ||
                            q.Contains("late") || q.Contains("past due") ||
                            q.Contains("behind") || q.Contains("at risk") ||
                            q.Contains("missed deadline") || q.Contains("exceeded deadline") ||
                            q.Contains("need attention") || q.Contains("problematic") ||
                            q.Contains("still open") || notOnTime;

        bool wantsIndeterminate = q.Contains("indeterminate") || q.Contains("missing date") ||
                                  q.Contains("no completion date") || q.Contains("without date") ||
                                  q.Contains("unknown status") || q.Contains("incomplete data") ||
                                  notOnTime;

        bool wantsOnTime = (q.Contains("on time") || q.Contains("met deadline") ||
                            q.Contains("ahead of schedule") || q.Contains("finished early") ||
                            q.Contains("completed early") || q.Contains("within deadline"))
                           && !notOnTime;

        var result = new List<string>(3);
        if (wantsOnTime)        result.Add("On Time");
        if (wantsDelayed)       result.Add("Delayed");
        if (wantsIndeterminate) result.Add("Indeterminate");

        return result.Count > 0 ? result : ["Delayed", "Indeterminate"];
    }
}

// ─── Gemini (free tier via Google AI Studio key) ──────────────────────────────

public class GeminiPlanGenerator(IConfiguration config, IServiceRegistry registry, ILogger<GeminiPlanGenerator> logger)
    : IPlanGenerator
{
    public string ProviderName => "Gemini 2.5 Flash";

    private readonly GenerativeModel _model = new GoogleAI(
            apiKey: config["Gemini:ApiKey"]
                ?? throw new InvalidOperationException(
                    "Gemini:ApiKey is not configured. " +
                    "Set via: dotnet user-secrets set \"Gemini:ApiKey\" \"AIza...\" " +
                    "or env var Gemini__ApiKey"))
        .GenerativeModel(
            model: "gemini-2.5-flash",
            generationConfig: new GenerationConfig { ResponseMimeType = "application/json" });

    private string BuildPrompt(string query)
    {
        var contracts = registry.GetAll();
        var coreInstructions = PromptBuilder.CoreInstructions(contracts);
        var exampleOps = PromptBuilder.ExampleOperations(contracts);

        return $$"""
You are a governed analytics plan generator for a laboratory information management system (LIMS).

{{coreInstructions}}

Respond ONLY with a valid JSON object — no markdown fences, no trailing text, no extra keys.
Use this exact structure. For a supported query set supported=true and populate plan.
For an unsupported query set supported=false, set reason, and set markdown and plan to null.

{
  "supported": true,
  "reason": null,
  "markdown": "string — markdown explanation of the plan steps",
  "plan": {
    "version": "1.0",
    "intent": "find_studies_not_completed_on_time",
    "entities": ["study","testp"],
    "operations": [
{{exampleOps}}
    ],
    "output": {
      "includeClassifications": ["Delayed", "Indeterminate"]
    },
    "limits": { "maxRows": 500, "pagination": false }
  }
}

User query: {{query}}
""";
    }

    public async Task<PlanGeneratorResult> GenerateAsync(string query)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            var prompt = BuildPrompt(query);
            logger.LogInformation("Gemini → query: {Query}", query);
            logger.LogInformation("Gemini → prompt:\n{Prompt}", prompt);
            var response = await _model.GenerateContent(prompt, cancellationToken: cts.Token);
            var raw = response.Text ?? string.Empty;
            logger.LogInformation("Gemini ← raw response:\n{Raw}", raw);

            // Extract the JSON object — robust against markdown fences or any leading/trailing text
            var startIdx = raw.IndexOf('{');
            var endIdx   = raw.LastIndexOf('}');
            if (startIdx < 0 || endIdx <= startIdx)
            {
                logger.LogError("Gemini response contained no JSON object. Raw: {Raw}", raw);
                return new PlanGeneratorResult(string.Empty, null,
                    "Plan generation service returned an unreadable response.", IsServerError: true);
            }
            var json = raw[startIdx..(endIdx + 1)];

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.GetProperty("supported").GetBoolean())
            {
                var reason = root.GetProperty("reason").GetString()
                             ?? "Query not supported by this assistant.";
                return new PlanGeneratorResult(string.Empty, null, reason);
            }

            var markdown = root.GetProperty("markdown").GetString() ?? string.Empty;
            var plan = JsonSerializer.Deserialize<ExecutionPlan>(
                root.GetProperty("plan").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (plan is null)
            {
                logger.LogError("Gemini returned supported=true but plan deserialised to null. Raw: {Json}", json);
                return new PlanGeneratorResult(string.Empty, null,
                    "Plan generation service returned an unexpected response.", IsServerError: true);
            }

            if (string.IsNullOrWhiteSpace(plan.Intent) || plan.Operations.Count == 0)
            {
                logger.LogWarning(
                    "Gemini returned supported=true with incomplete plan (intent='{Intent}', ops={Ops}). Treating as unsupported. Raw: {Json}",
                    plan.Intent, plan.Operations.Count, json);
                return new PlanGeneratorResult(string.Empty, null, "Query not supported by this assistant.");
            }

            logger.LogInformation("Gemini ← plan ready | intent={Intent} | services={Services} | classifications={Classifications}",
                plan.Intent,
                string.Join(", ", plan.Operations.Select(o => o.Service)),
                string.Join(", ", plan.Output?.IncludeClassifications ?? []));

            return new PlanGeneratorResult(markdown, plan, null);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Gemini response failed JSON parse for query: {Query}", query);
            return new PlanGeneratorResult(string.Empty, null,
                "Plan generation service returned an unreadable response.", IsServerError: true);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Gemini call timed out after 45s for query: {Query}", query);
            return new PlanGeneratorResult(string.Empty, null,
                "Plan generation timed out. Gemini free tier can be slow — please try again.", IsServerError: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Gemini call failed for query: {Query}", query);
            return new PlanGeneratorResult(string.Empty, null,
                "Plan generation service is temporarily unavailable.", IsServerError: true);
        }
    }
}

// ─── OpenAI (strict JSON schema output) ──────────────────────────────────────

public class OpenAiPlanGenerator(IConfiguration config, IServiceRegistry registry, ILogger<OpenAiPlanGenerator> logger)
    : IPlanGenerator
{
    public string ProviderName => "GPT-4o Mini";

    private readonly ChatClient _client = new ChatClient("gpt-4o-mini",
        config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException(
                "OpenAI:ApiKey is not configured. " +
                "Set via: dotnet user-secrets set \"OpenAI:ApiKey\" \"sk-...\" " +
                "or env var OpenAI__ApiKey"));

    // Strict schema — operation items include "reason" so the model always fills it.
    private static readonly BinaryData ResponseSchema = BinaryData.FromString("""
    {
      "type": "object",
      "properties": {
        "supported": { "type": "boolean" },
        "reason":   { "anyOf": [{ "type": "string" }, { "type": "null" }] },
        "markdown": { "anyOf": [{ "type": "string" }, { "type": "null" }] },
        "plan": {
          "anyOf": [
            {
              "type": "object",
              "properties": {
                "version":  { "type": "string" },
                "intent":   { "type": "string" },
                "entities": { "type": "array", "items": { "type": "string" } },
                "operations": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "service": { "type": "string" },
                      "action":  { "type": "string" },
                      "select":  { "type": "array", "items": { "type": "string" } },
                      "filters": {
                        "type": "array",
                        "items": {
                          "type": "object",
                          "properties": {
                            "field": { "type": "string" },
                            "op":    { "type": "string" },
                            "value": { "anyOf": [{ "type": "string" }, { "type": "null" }] }
                          },
                          "required": ["field", "op", "value"],
                          "additionalProperties": false
                        }
                      },
                      "reason": { "anyOf": [{ "type": "string" }, { "type": "null" }] }
                    },
                    "required": ["service", "action", "select", "filters", "reason"],
                    "additionalProperties": false
                  }
                },
                "output": {
                  "type": "object",
                  "properties": {
                    "includeClassifications": {
                      "type": "array",
                      "items": { "type": "string" }
                    }
                  },
                  "required": ["includeClassifications"],
                  "additionalProperties": false
                },
                "limits": {
                  "type": "object",
                  "properties": {
                    "maxRows":    { "type": "integer" },
                    "pagination": { "type": "boolean" }
                  },
                  "required": ["maxRows", "pagination"],
                  "additionalProperties": false
                }
              },
              "required": ["version", "intent", "entities", "operations", "output", "limits"],
              "additionalProperties": false
            },
            { "type": "null" }
          ]
        }
      },
      "required": ["supported", "reason", "markdown", "plan"],
      "additionalProperties": false
    }
    """);

    public async Task<PlanGeneratorResult> GenerateAsync(string query)
    {
        try
        {
            var systemPrompt =
                "You are a governed analytics plan generator for a laboratory information management system (LIMS).\n\n" +
                PromptBuilder.CoreInstructions(registry.GetAll());

            var completion = await _client.CompleteChatAsync(
                [new SystemChatMessage(systemPrompt), new UserChatMessage(query)],
                new ChatCompletionOptions
                {
                    ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                        "plan_response", ResponseSchema, jsonSchemaIsStrict: true)
                });

            var json = completion.Value.Content[0].Text;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.GetProperty("supported").GetBoolean())
            {
                var reason = root.GetProperty("reason").GetString()
                             ?? "Query not supported by this assistant.";
                return new PlanGeneratorResult(string.Empty, null, reason);
            }

            var markdown = root.GetProperty("markdown").GetString() ?? string.Empty;
            var plan = JsonSerializer.Deserialize<ExecutionPlan>(
                root.GetProperty("plan").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (plan is null)
            {
                logger.LogError("OpenAI returned supported=true but plan deserialised to null. Raw: {Json}", json);
                return new PlanGeneratorResult(string.Empty, null,
                    "Plan generation service returned an unexpected response.", IsServerError: true);
            }

            return new PlanGeneratorResult(markdown, plan, null);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "OpenAI response failed JSON parse for query: {Query}", query);
            return new PlanGeneratorResult(string.Empty, null,
                "Plan generation service returned an unreadable response.", IsServerError: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpenAI call failed for query: {Query}", query);
            return new PlanGeneratorResult(string.Empty, null,
                "Plan generation service is temporarily unavailable.", IsServerError: true);
        }
    }
}

// ─── OpenRouter (OpenAI-compatible endpoint, free/cheap models) ───────────────

public class OpenRouterPlanGenerator(IConfiguration config, IServiceRegistry registry, ILogger<OpenRouterPlanGenerator> logger)
    : IPlanGenerator
{
    private readonly string _model =
        config["OpenRouter:Model"] ?? "mistralai/mistral-7b-instruct";

    public string ProviderName => $"OpenRouter ({config["OpenRouter:Model"] ?? "mistral-7b-instruct"})";

    private readonly ChatClient _client = new ChatClient(
        config["OpenRouter:Model"] ?? "mistralai/mistral-7b-instruct",
        new System.ClientModel.ApiKeyCredential(
            config["OpenRouter:ApiKey"]
                ?? throw new InvalidOperationException(
                    "OpenRouter:ApiKey is not configured. " +
                    "Set via: dotnet user-secrets set \"OpenRouter:ApiKey\" \"sk-or-...\" " +
                    "or env var OpenRouter__ApiKey")),
        new OpenAI.OpenAIClientOptions { Endpoint = new Uri("https://openrouter.ai/api/v1") });

    public async Task<PlanGeneratorResult> GenerateAsync(string query)
    {
        const int maxAttempts = 3;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        var systemPrompt =
            "You are a governed analytics plan generator for a laboratory information management system (LIMS).\n\n" +
            PromptBuilder.CoreInstructions(registry.GetAll()) +
            "\n\nRespond ONLY with a valid JSON object — no markdown fences, no trailing text.\n" +
            "For a supported query set supported=true and populate plan.\n" +
            "For an unsupported query set supported=false, set reason, and set markdown and plan to null.\n" +
            "IMPORTANT: Keep the markdown field to ONE short sentence (max 20 words). Do not write bullet points or steps.";

        logger.LogInformation("OpenRouter ({Model}) → query: {Query}", _model, query);
        logger.LogInformation("OpenRouter → system prompt:\n{Prompt}", systemPrompt);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (attempt > 1)
                    logger.LogWarning("OpenRouter → retry attempt {Attempt}/{Max} for query: {Query}", attempt, maxAttempts, query);

                var completion = await _client.CompleteChatAsync(
                    [new SystemChatMessage(systemPrompt), new UserChatMessage(query)],
                    new ChatCompletionOptions
                    {
                        ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
                        MaxOutputTokenCount = 1024
                    },
                    cts.Token);

                if (completion.Value.Content.Count == 0 || string.IsNullOrWhiteSpace(completion.Value.Content[0].Text))
                {
                    logger.LogWarning("OpenRouter returned an empty response body (attempt {Attempt}).", attempt);
                    if (attempt < maxAttempts) continue;
                    return new PlanGeneratorResult(string.Empty, null,
                        "AI provider returned an empty response — the model may be rate-limited. Try again or switch to Mock.", IsServerError: true);
                }

                var raw = completion.Value.Content[0].Text;
                logger.LogInformation("OpenRouter ← raw response (attempt {Attempt}):\n{Raw}", attempt, raw);

                var startIdx = raw.IndexOf('{');
                var endIdx   = raw.LastIndexOf('}');
                if (startIdx < 0 || endIdx <= startIdx)
                {
                    logger.LogWarning("OpenRouter response contained no JSON object (attempt {Attempt}). Raw: {Raw}", attempt, raw);
                    if (attempt < maxAttempts) continue;
                    return new PlanGeneratorResult(string.Empty, null,
                        "Plan generation service returned an unreadable response.", IsServerError: true);
                }
                var json = raw[startIdx..(endIdx + 1)];

                JsonDocument doc;
                try { doc = JsonDocument.Parse(json); }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "OpenRouter JSON parse failed (attempt {Attempt}). Raw: {Json}", attempt, json);
                    if (attempt < maxAttempts) continue;
                    return new PlanGeneratorResult(string.Empty, null,
                        "Plan generation service returned an unreadable response.", IsServerError: true);
                }

                using (doc)
                {
                    var root = doc.RootElement;

                    if (!root.GetProperty("supported").GetBoolean())
                    {
                        var reason = root.GetProperty("reason").GetString()
                                     ?? "Query not supported by this assistant.";
                        return new PlanGeneratorResult(string.Empty, null, reason);
                    }

                    var markdown = root.GetProperty("markdown").GetString() ?? string.Empty;
                    var plan = JsonSerializer.Deserialize<ExecutionPlan>(
                        root.GetProperty("plan").GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (plan is null)
                    {
                        logger.LogWarning("OpenRouter plan deserialised to null (attempt {Attempt}). Raw: {Json}", attempt, json);
                        if (attempt < maxAttempts) continue;
                        return new PlanGeneratorResult(string.Empty, null,
                            "Plan generation service returned an unexpected response.", IsServerError: true);
                    }

                    if (string.IsNullOrWhiteSpace(plan.Intent) || plan.Operations.Count == 0)
                    {
                        logger.LogWarning("OpenRouter returned incomplete plan (attempt {Attempt}). Raw: {Json}", attempt, json);
                        if (attempt < maxAttempts) continue;
                        return new PlanGeneratorResult(string.Empty, null, "Query not supported by this assistant.");
                    }

                    logger.LogInformation("OpenRouter ← plan ready | intent={Intent} | services={Services} | classifications={Classifications}",
                        plan.Intent,
                        string.Join(", ", plan.Operations.Select(o => o.Service)),
                        string.Join(", ", plan.Output?.IncludeClassifications ?? []));

                    return new PlanGeneratorResult(markdown, plan, null);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("OpenRouter call timed out after 45s for query: {Query}", query);
                return new PlanGeneratorResult(string.Empty, null,
                    "Plan generation timed out — please try again.", IsServerError: true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OpenRouter call failed (attempt {Attempt}) for query: {Query}", attempt, query);
                if (attempt < maxAttempts) continue;
                return new PlanGeneratorResult(string.Empty, null,
                    "Plan generation service is temporarily unavailable.", IsServerError: true);
            }
        }

        return new PlanGeneratorResult(string.Empty, null,
            "Plan generation service is temporarily unavailable.", IsServerError: true);
    }
}
