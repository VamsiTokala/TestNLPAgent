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
            sb.AppendLine($"  {c.Name} → action: {c.Action}");
            sb.AppendLine($"    Purpose: {c.Purpose}");
            sb.AppendLine($"    Fields: {string.Join(", ", c.Fields)}");
            if (c.FieldExamples is { Count: > 0 })
            {
                foreach (var (field, values) in c.FieldExamples)
                {
                    // Identifier-shaped columns (e.g. studyId, studyCode) have many
                    // possible values; treat the list as a SAMPLE that shows the
                    // shape. Other columns (status, result, ...) are enumerations.
                    var isIdentifier = field.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
                                       || field.EndsWith("Code", StringComparison.OrdinalIgnoreCase);
                    var label = isIdentifier
                        ? $"Example values for {field} (samples only \u2014 not exhaustive)"
                        : $"Allowed values for {field} (exhaustive)";
                    sb.AppendLine($"    {label}: {string.Join(", ", values)}");
                }
            }
        }
        return sb.ToString().TrimEnd();
    }

    // Builds the example operations array for the JSON schema embedded in the Gemini prompt.
    internal static string ExampleOperations(IReadOnlyList<ServiceContractEntry> contracts)
    {
        var ops = contracts.Select(c =>
        {
            var select = string.Join(", ", c.Fields.Select(f => $"\"{f}\""));
            var filter = c.Fields.Any(f => f.Equals("status", StringComparison.OrdinalIgnoreCase))
                ? "[{\"field\":\"status\",\"op\":\"=\",\"value\":\"<value>\"}]"
                : "[]";
            return $"      {{ \"service\": \"{c.Name}\", \"action\": \"{c.Action}\", \"select\": [{select}], \"filters\": {filter}, \"reason\": \"why this contract is needed for the query\" }}";
        });
        return string.Join(",\n", ops);
    }

    // Full response shape — every field name the deserialiser expects.
    // Use this for providers that have no native JSON-schema enforcement.
    internal static string ResponseShape(IReadOnlyList<ServiceContractEntry> contracts) => $$"""
{
  "supported": true,
  "reason": null,
  "markdown": "ONE short sentence describing the plan",
  "plan": {
    "version": "1.0",
    "intent": "<snake_case_verb_phrase_derived_from_the_query>",
    "entities": ["<entity1>", "<entity2_if_join>"],
    "primaryEntity": "<contract-name-the-result-is-about>",
    "operations": [
{{ExampleOperations(contracts)}}
    ],
    "correlate": { "leftEntity": "", "rightEntity": "", "leftField": "", "rightField": "" },
    "output": { "includeClassifications": ["On Time", "Delayed", "Indeterminate"] },
    "limits": { "maxRows": 500, "pagination": false }
  }
}
Field rules: each operation MUST use keys "service", "action", "select", "filters", "reason".
Do NOT rename them to "entity", "type", "function", or "condition". "service" MUST be one of the contract names listed above.
"Allowed values for <field> (exhaustive)" lines are a strict whitelist — only those literal values may be used as filter values for that field.
"Example values for <field> (samples only — not exhaustive)" lines show the SHAPE of identifier values — user identifiers that match the shape MUST be accepted even if the exact value is not in the sample list. Never declare an identifier 'unsupported' just because it isn't in the sample list.
""";

    // The invariant sections shared across Gemini and OpenAI prompts.
    internal static string CoreInstructions(IReadOnlyList<ServiceContractEntry> contracts)
    {
        var servicesBlock = ServicesBlock(contracts);
        return $"""
REGISTERED SERVICE CONTRACTS — these are the ONLY data sources available:
{servicesBlock}

ALLOWED FILTER OPERATORS: =, !=, >, >=, <, <=, in, between, is null, is not null
ALLOWED AGGREGATE FUNCTIONS: max, min, count, sum, avg
MAX ROWS LIMIT: 500

DOMAIN
Answer any question that can be served by one or more of the registered contracts above:
- Single-contract:  filter, count, list, aggregate rows from one contract
- Multi-contract:   join two or more contracts on a shared field; include all
                    contracts needed to answer the question
- Timeliness:       classify records as On Time / Delayed / Indeterminate when
                    the question asks about completion deadlines or lateness

SUPPORTED = TRUE for any query that can be answered using the registered contracts.
SUPPORTED = FALSE only when the query asks about something no registered contract
covers (weather, invoices, HR records, unrelated equipment, recipes, etc.).

CLASSIFICATION RULES (use ONLY when the query is about completion timeliness):
- "On Time":        actualCompletionDate <= plannedCompletionDate (both present)
- "Delayed":        actualCompletionDate >  plannedCompletionDate (both present)
- "Indeterminate":  plannedCompletionDate is null OR actualCompletionDate is null

BUILDING THE PLAN
1. Pick ONLY the contracts whose data the query actually needs. Do not include
   unrelated contracts. For a join, include both sides on a shared field.
2. Set intent to a short snake_case verb-phrase that describes what the user wants
   — derive it from the query, do NOT use a fixed template.
3. Set primaryEntity to the contract name whose ROWS the answer is about
   (e.g. "count testps where ..." → primaryEntity is the testp contract; "list
   studies that ..." → primaryEntity is the study contract).
4. For joins, populate correlate with leftEntity, rightEntity, leftField, rightField
   identifying the shared key between the two contracts.
5. Set output.includeClassifications ONLY when the question asks WHICH records
   are on time / delayed / indeterminate (timeliness is the answer or the
   filter). Use this mapping:
   - delayed / late / overdue / not-on-time / behind  → ["Delayed", "Indeterminate"]
   - only delayed                                      → ["Delayed"]
   - only indeterminate / missing data                 → ["Indeterminate"]
   - on time / met deadline / finished early           → ["On Time"]
   For plain counts, lists, listings, or filters that have nothing to do with
   timeliness, leave includeClassifications as [] — do NOT default to all three.
   When the user counts or lists records of one entity and timeliness applies to
   a different entity (e.g. "count testps where parent study is on time"),
   STILL set includeClassifications to filter by parent timeliness — the engine
   will apply it to primaryEntity rows via the join.
6. Filter values:
   - For fields shown with "Allowed values for <field> (exhaustive)": use ONLY
     one of those literal values; if the user's word doesn't match, omit the filter.
   - For fields shown with "Example values for <field> (samples only)": those
     are PATTERN hints. Accept any user-supplied identifier whose shape matches
     (e.g. "ST-006" matches studyCode pattern "ST-###"; "S6" matches studyId
     pattern "S#"). Do NOT mark the query unsupported because an identifier
     isn't in the sample list — the actual data contains many more values.
   - If the identifier belongs to a different contract than the primaryEntity,
     include that contract too and correlate the two on their shared key
     (e.g. studyId).
7. Fill "reason" on each operation explaining why that contract is needed.

OUTPUT
If supported = true:
  - reason   = null
  - markdown = ONE short sentence summarising the plan (no bullets, no steps)
  - plan     = fully populated (intent, entities, operations with reasons,
               correlate if a join, output.includeClassifications, limits)

If supported = false:
  - reason   = one sentence explaining why this is outside the domain
  - markdown = null
  - plan     = null
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

        var allContracts = registry.GetAll();

        // Determine which contracts are relevant to the query
        var selectedContracts = SelectContracts(q, allContracts);
        var intent            = DeriveIntent(q);
        var classifications   = ResolveClassifications(q);

        if (!IsSupportedQuery(q, intent, classifications))
            return Task.FromResult(new PlanGeneratorResult(string.Empty, null,
                "Query not supported by this assistant."));

        // Build a short, query-derived markdown summary
        var contractNames = string.Join(" + ", selectedContracts.Select(c => c.DisplayName));
        var markdown = $"Fetching data from {contractNames} to answer: {query}.";

        var plan = new ExecutionPlan
        {
            Intent   = intent,
            Entities = selectedContracts.Select(c => c.Name.Split('-')[0]).Distinct().ToList(),
            Operations = selectedContracts.Select(c => new PlanOperation(
                Service: c.Name,
                Action:  c.Action,
                Select:  [.. c.Fields],
                Filters: [],
                Reason:  $"Provides {c.Purpose.ToLowerInvariant()}"
            )).ToList(),
            Output = new PlanOutput(classifications)
        };

        logger.LogInformation(
            "Mock ← plan ready | intent={Intent} | services={Services} | classifications={Classifications}",
            intent,
            string.Join(", ", selectedContracts.Select(c => c.Name)),
            string.Join(", ", classifications));

        return Task.FromResult(new PlanGeneratorResult(markdown, plan, null));
    }

    private static bool IsSupportedQuery(string q, string intent, List<string> classifications)
    {
        if (intent == "query_records")
            return false;

        if (q.Contains("filter") && classifications.Count == 0)
            return false;

        return true;
    }

    // Select which registered contracts are relevant based on keywords in the query.
    // Falls back to ALL contracts so the Mock never silently drops data.
    private static List<ServiceContractEntry> SelectContracts(
        string q, IReadOnlyList<ServiceContractEntry> all)
    {
        var selected = all.Where(c =>
        {
            var name    = c.Name.ToLowerInvariant();
            var display = c.DisplayName.ToLowerInvariant();
            var words   = new[] { name, display }
                .Concat(c.Fields.Select(f => f.ToLowerInvariant()))
                .Concat(c.Purpose.ToLowerInvariant().Split(' '));
            return words.Any(w => w.Length > 2 && q.Contains(w));
        }).ToList();

        // If nothing matched, include all required contracts plus the first optional one
        // so the Mock always returns something useful
        if (selected.Count == 0)
            selected = all.Where(c => c.IsRequired).ToList();
        if (selected.Count == 0)
            selected = [all[0]];

        return selected;
    }

    private static string DeriveIntent(string q)
    {
        // Map recognisable phrases to tidy intent slugs
        if (q.Contains("not on time") || q.Contains("not completed on time") || q.Contains("late"))
            return "find_records_not_completed_on_time";
        if (q.Contains("delayed") || q.Contains("overdue") || q.Contains("past due"))
            return "find_delayed_records";
        if (q.Contains("indeterminate") || q.Contains("missing date") || q.Contains("no completion"))
            return "find_indeterminate_records";
        if (q.Contains("on time") || q.Contains("met deadline") || q.Contains("completed early"))
            return "find_on_time_records";
        if (q.Contains("count") || q.Contains("how many") || q.Contains("total"))
            return "count_records";
        if (q.Contains("list") || q.Contains("show") || q.Contains("find") || q.Contains("get"))
            return "list_records";
        if (q.Contains("summary") || q.Contains("overview") || q.Contains("breakdown"))
            return "summarise_records";
        return "query_records";
    }

    private static List<string> ResolveClassifications(string q)
    {
        bool notOnTime = q.Contains("not on time") || q.Contains("not completed on time") ||
                         q.Contains("haven't finished") || q.Contains("not finished");

        bool wantsDelayed = q.Contains("delayed") || q.Contains("overdue") || q.Contains("late") ||
                            q.Contains("past due") || q.Contains("behind") || q.Contains("at risk") ||
                            q.Contains("missed deadline") || notOnTime;

        bool wantsIndeterminate = q.Contains("indeterminate") || q.Contains("missing date") ||
                                  q.Contains("no completion date") || q.Contains("without date") ||
                                  q.Contains("unknown status") || q.Contains("incomplete data") ||
                                  notOnTime;

        bool wantsOnTime = (q.Contains("on time") || q.Contains("met deadline") ||
                            q.Contains("ahead of schedule") || q.Contains("finished early") ||
                            q.Contains("completed early") || q.Contains("within deadline"))
                           && !notOnTime;

        bool isOverview = q.Contains("all ") || q.Contains("show all") || q.Contains("how many") ||
                          q.Contains("count") || q.Contains("summary") || q.Contains("breakdown") ||
                          q.Contains("overview") || q.Contains("dashboard") || q.Contains("total") ||
                          q.Contains("list ") || q.Contains("find ");

        if (isOverview && !wantsDelayed && !wantsIndeterminate && !wantsOnTime)
            return ["On Time", "Delayed", "Indeterminate"];

        var result = new List<string>(3);
        if (wantsOnTime)        result.Add("On Time");
        if (wantsDelayed)       result.Add("Delayed");
        if (wantsIndeterminate) result.Add("Indeterminate");

        return result.Count > 0 ? result : ["On Time", "Delayed", "Indeterminate"];
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
        var responseShape = PromptBuilder.ResponseShape(contracts);
        var asOfDate = DateTime.UtcNow.ToString("yyyy-MM-dd");

        return $$"""
You are a governed analytics plan generator over the registered service contracts below.

    REFERENCE DATE
    Treat relative date phrases using UTC date {{asOfDate}}.
    - "today" means {{asOfDate}}
    - "this year" means the calendar year containing {{asOfDate}}
    - "this month" means the calendar month containing {{asOfDate}}
    - "last year" and "last month" are relative to {{asOfDate}}

{{coreInstructions}}

Respond ONLY with a valid JSON object — no markdown fences, no trailing text, no extra keys.
Use this exact structure. For a supported query set supported=true and populate plan.
For an unsupported query set supported=false, set reason, and set markdown and plan to null.

{{responseShape}}

User query: {{query}}
""";
    }

    public async Task<PlanGeneratorResult> GenerateAsync(string query)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
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
                "primaryEntity": { "type": "string" },
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
              "required": ["version", "intent", "entities", "primaryEntity", "operations", "output", "limits"],
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
                "You are a governed analytics plan generator over the registered service contracts below.\n\n" +
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
        var attemptTimeout = TimeSpan.FromSeconds(75);
        var retryDelay = TimeSpan.FromMinutes(1);

        var contracts = registry.GetAll();
        var systemPrompt =
            "You are a governed analytics plan generator over the registered service contracts below.\n\n" +
            PromptBuilder.CoreInstructions(contracts) +
            "\n\nRespond ONLY with a valid JSON object — no markdown fences, no trailing text.\n" +
            "For a supported query set supported=true and populate plan.\n" +
            "For an unsupported query set supported=false, set reason, and set markdown and plan to null.\n" +
            "Keep the markdown field to ONE short sentence (max 20 words). No bullet points, no steps.\n\n" +
            "REQUIRED RESPONSE SHAPE — use these exact key names:\n" +
            PromptBuilder.ResponseShape(contracts);

        logger.LogInformation("OpenRouter ({Model}) → query: {Query}", _model, query);
        logger.LogInformation("OpenRouter → system prompt:\n{Prompt}", systemPrompt);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (attempt > 1)
                    logger.LogWarning("OpenRouter → retry attempt {Attempt}/{Max} for query: {Query}", attempt, maxAttempts, query);

                using var cts = new CancellationTokenSource(attemptTimeout);

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
                    if (attempt < maxAttempts)
                    {
                        await DelayBeforeRetryAsync(attempt, maxAttempts, retryDelay);
                        continue;
                    }
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
                    if (attempt < maxAttempts)
                    {
                        await DelayBeforeRetryAsync(attempt, maxAttempts, retryDelay);
                        continue;
                    }
                    return new PlanGeneratorResult(string.Empty, null,
                        "Plan generation service returned an unreadable response.", IsServerError: true);
                }
                var json = raw[startIdx..(endIdx + 1)];

                JsonDocument doc;
                try { doc = JsonDocument.Parse(json); }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "OpenRouter JSON parse failed (attempt {Attempt}). Raw: {Json}", attempt, json);
                    if (attempt < maxAttempts)
                    {
                        await DelayBeforeRetryAsync(attempt, maxAttempts, retryDelay);
                        continue;
                    }
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
                        if (attempt < maxAttempts)
                        {
                            await DelayBeforeRetryAsync(attempt, maxAttempts, retryDelay);
                            continue;
                        }
                        return new PlanGeneratorResult(string.Empty, null,
                            "Plan generation service returned an unexpected response.", IsServerError: true);
                    }

                    if (string.IsNullOrWhiteSpace(plan.Intent) || plan.Operations.Count == 0)
                    {
                        logger.LogWarning("OpenRouter returned incomplete plan (attempt {Attempt}). Raw: {Json}", attempt, json);
                        if (attempt < maxAttempts)
                        {
                            await DelayBeforeRetryAsync(attempt, maxAttempts, retryDelay);
                            continue;
                        }
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
                logger.LogWarning(
                    "OpenRouter call timed out after {TimeoutSeconds}s (attempt {Attempt}/{Max}) for query: {Query}",
                    attemptTimeout.TotalSeconds, attempt, maxAttempts, query);
                if (attempt < maxAttempts)
                {
                    await DelayBeforeRetryAsync(attempt, maxAttempts, retryDelay);
                    continue;
                }
                return new PlanGeneratorResult(string.Empty, null,
                    "Plan generation timed out — free-tier models can be slow. Please try again or switch to Mock.", IsServerError: true);
            }
            catch (System.ClientModel.ClientResultException ex) when (ex.Status == 401 || ex.Status == 403)
            {
                logger.LogError(ex, "OpenRouter authentication failed for query: {Query}", query);
                return new PlanGeneratorResult(string.Empty, null,
                    "OpenRouter authentication failed. Check OpenRouter:ApiKey and account access.", IsServerError: true);
            }
            catch (System.ClientModel.ClientResultException ex) when (ex.Status == 429)
            {
                logger.LogWarning(ex, "OpenRouter rate limited for query: {Query}", query);
                return new PlanGeneratorResult(string.Empty, null,
                    "OpenRouter rate limit reached or account has no available credits/quota. Try later, switch model, or use Gemini.", IsServerError: true);
            }
            catch (System.ClientModel.ClientResultException ex) when (ex.Status == 503)
            {
                logger.LogWarning(ex, "OpenRouter provider unavailable for model {Model} and query: {Query}", _model, query);
                return new PlanGeneratorResult(string.Empty, null,
                    $"OpenRouter model '{_model}' is currently unavailable. Try again later, switch model, or use Gemini.", IsServerError: true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OpenRouter call failed (attempt {Attempt}) for query: {Query}", attempt, query);
                if (attempt < maxAttempts)
                {
                    await DelayBeforeRetryAsync(attempt, maxAttempts, retryDelay);
                    continue;
                }
                return new PlanGeneratorResult(string.Empty, null,
                    "Plan generation service is temporarily unavailable.", IsServerError: true);
            }
        }

        return new PlanGeneratorResult(string.Empty, null,
            "Plan generation service is temporarily unavailable.", IsServerError: true);
    }

    private async Task DelayBeforeRetryAsync(int attempt, int maxAttempts, TimeSpan retryDelay)
    {
        logger.LogWarning(
            "OpenRouter waiting {DelaySeconds}s before retry ({NextAttempt}/{Max}).",
            retryDelay.TotalSeconds, attempt + 1, maxAttempts);
        await Task.Delay(retryDelay);
    }
}
