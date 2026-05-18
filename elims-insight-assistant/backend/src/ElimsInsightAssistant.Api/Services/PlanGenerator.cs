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
    Task<PlanGeneratorResult> GenerateAsync(string query);
}

// ─── Mock (no API key required — local dev and tests) ────────────────────────

public class MockPlanGenerator : IPlanGenerator
{
    private static readonly string[] SupportedTerms =
    [
        "not completed on time", "delayed studies", "completed late", "not on time",
        "indeterminate", "classification indeterminate", "classification delayed",
        "classification on time", "filter studies", "show delayed", "show indeterminate",
        "show on time", "show all studies"
    ];

    public Task<PlanGeneratorResult> GenerateAsync(string query)
    {
        var q = query.ToLowerInvariant();
        if (!SupportedTerms.Any(q.Contains))
            return Task.FromResult(new PlanGeneratorResult(
                string.Empty, null,
                "This demo currently supports queries related to study completion timeliness."));

        var classifications = ResolveClassifications(q);

        var markdown = $"""
# Analysis Plan
Intent: Find studies not completed on time.

## Contract mapping
- Study Service: study identity, customer, legal entity, and planned completion date.
- CoreLabs Service: TestP status and completion timestamps.

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
            Operations =
            [
                new("study-service", "listStudies",
                    ["studyId", "studyCode", "customer", "legalEntity", "plannedCompletionDate"], []),
                new("corelabs-service", "listTestPs",
                    ["testpId", "studyId", "status", "completedAt", "runType", "result"],
                    [new("status", "=", "Completed")])
            ],
            Output = new PlanOutput(classifications)
        };

        return Task.FromResult(new PlanGeneratorResult(markdown, plan, null));
    }

    private static List<string> ResolveClassifications(string q)
    {
        // Explicit "classification X" pattern takes precedence
        if (q.Contains("classification indeterminate")) return ["Indeterminate"];
        if (q.Contains("classification delayed")) return ["Delayed"];
        if (q.Contains("classification on time")) return ["On Time"];

        // "show all" or combinations
        if (q.Contains("show all studies") || (q.Contains("on time") && q.Contains("delayed") && q.Contains("indeterminate")))
            return ["On Time", "Delayed", "Indeterminate"];

        // Single-classification requests
        bool wantsDelayed     = q.Contains("delayed") || q.Contains("completed late") || q.Contains("not on time") || q.Contains("not completed on time");
        bool wantsIndeterminate = q.Contains("indeterminate");
        bool wantsOnTime      = q.Contains("on time") && !wantsDelayed;

        if (wantsOnTime && !wantsDelayed && !wantsIndeterminate) return ["On Time"];
        if (wantsDelayed && !wantsIndeterminate) return ["Delayed"];
        if (wantsIndeterminate && !wantsDelayed) return ["Indeterminate"];

        return ["Delayed", "Indeterminate"];
    }
}

// ─── Gemini (free tier via Google AI Studio key — hundreds of queries/day) ────

public class GeminiPlanGenerator : IPlanGenerator
{
    private readonly GenerativeModel _model;
    private readonly ILogger<GeminiPlanGenerator> _logger;

    // Gemini does not have OpenAI-style strict schema enforcement, so we embed the
    // required JSON structure directly in the prompt and use ResponseMimeType=application/json.
    // The response will be clean JSON; we strip any accidental markdown fences defensively.
    private const string FullPrompt = """
You are a governed analytics plan generator for a laboratory information management system (LIMS).

Given a natural language query, decide whether it is asking about study completion timeliness
or filtering studies by their completion classification (On Time, Delayed, Indeterminate).

SUPPORTED QUERY TYPES (set supported = true):
- Studies not completed on time: delayed, overdue, late, missed deadline
- Indeterminate studies: missing planned or actual completion date
- Filter/show studies by classification: "filter studies with classification X",
  "show delayed studies", "show indeterminate studies", "show on time studies"
- Any combination of the above classifications

ALLOWED SERVICES AND FIELDS:
  study-service    → action: listStudies → fields: studyId, studyCode, customer, legalEntity, plannedCompletionDate
  corelabs-service → action: listTestPs  → fields: testpId, studyId, status, completedAt, runType, result

ALLOWED FILTER OPERATORS: =, !=, >, >=, <, <=, in, between, is null, is not null
ALLOWED AGGREGATE FUNCTIONS: max, min, count, sum, avg
MAX ROWS LIMIT: 500

CLASSIFICATION RULES:
- "On Time": actualCompletionDate <= plannedCompletionDate (both dates present)
- "Delayed": actualCompletionDate > plannedCompletionDate (both dates present)
- "Indeterminate": plannedCompletionDate is null OR actualCompletionDate is null

SET output.includeClassifications based on the query intent:
- "show delayed studies" or "not completed on time" or "late" → ["Delayed", "Indeterminate"]
- "show only delayed" or "filter studies with classification Delayed" → ["Delayed"]
- "show only indeterminate" or "filter studies with classification Indeterminate" → ["Indeterminate"]
- "show on time studies" or "filter studies with classification On Time" → ["On Time"]
- "show all studies" or all three classifications requested → ["On Time", "Delayed", "Indeterminate"]

If the query IS about study completion timeliness or classification filtering:
  - Set supported = true
  - Set reason = null
  - Write a clear markdown plan explaining the steps
  - Populate plan with version "1.0", intent "find_studies_not_completed_on_time", the two operations,
    output.includeClassifications set according to the rules above, and limits maxRows 500

If the query is NOT about study completion timeliness:
  - Set supported = false
  - Set reason to a one-sentence explanation
  - Set markdown = null and plan = null

Respond ONLY with a JSON object matching this exact structure (no markdown fences, no extra keys):
{
  "supported": true|false,
  "reason": null or "string",
  "markdown": null or "string",
  "plan": null or {
    "version": "1.0",
    "intent": "find_studies_not_completed_on_time",
    "entities": ["study","testp"],
    "operations": [
      { "service": "study-service", "action": "listStudies", "select": [...], "filters": [] },
      { "service": "corelabs-service", "action": "listTestPs", "select": [...], "filters": [{"field":"status","op":"=","value":"Completed"}] }
    ],
    "output": {
      "includeClassifications": ["Delayed", "Indeterminate"]
    },
    "limits": { "maxRows": 500, "pagination": false }
  }
}

User query: {QUERY}
""";

    public GeminiPlanGenerator(IConfiguration config, ILogger<GeminiPlanGenerator> logger)
    {
        _logger = logger;
        var apiKey = config["Gemini:ApiKey"]
            ?? throw new InvalidOperationException(
                "Gemini:ApiKey is not configured. " +
                "Set via: dotnet user-secrets set \"Gemini:ApiKey\" \"AIza...\" " +
                "or env var Gemini__ApiKey");

        var googleAi = new GoogleAI(apiKey: apiKey);
        _model = googleAi.GenerativeModel(
            model: "gemini-2.5-flash",
            generationConfig: new GenerationConfig { ResponseMimeType = "application/json" });
    }

    public async Task<PlanGeneratorResult> GenerateAsync(string query)
    {
        try
        {
            var prompt = FullPrompt.Replace("{QUERY}", query);
            var response = await _model.GenerateContent(prompt);
            var raw = response.Text ?? string.Empty;

            // Strip accidental markdown fences (defensive — JSON mode should not need this)
            var json = raw.Trim();
            if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
            if (json.EndsWith("```")) json = json[..json.LastIndexOf("```")].TrimEnd();

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
                _logger.LogError("Gemini returned supported=true but plan deserialised to null. Raw: {Json}", json);
                return new PlanGeneratorResult(string.Empty, null,
                    "Plan generation service returned an unexpected response.", IsServerError: true);
            }

            return new PlanGeneratorResult(markdown, plan, null);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Gemini response failed JSON parse for query: {Query}", query);
            return new PlanGeneratorResult(string.Empty, null,
                "Plan generation service returned an unreadable response.", IsServerError: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini call failed for query: {Query}", query);
            return new PlanGeneratorResult(string.Empty, null,
                "Plan generation service is temporarily unavailable.", IsServerError: true);
        }
    }
}

// ─── OpenAI (real NL intent extraction with strict JSON schema output) ────────

public class OpenAiPlanGenerator : IPlanGenerator
{
    private readonly ChatClient _client;
    private readonly ILogger<OpenAiPlanGenerator> _logger;

    // Strict JSON schema constrains the model's output to exactly the shape we need.
    // This eliminates the need to handle free-form text, markdown fences, or partial responses.
    // The validator still runs after — this is defence in depth, not a replacement.
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
                      }
                    },
                    "required": ["service", "action", "select", "filters"],
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

    private const string SystemPrompt = """
You are a governed analytics plan generator for a laboratory information management system (LIMS).

Given a natural language query, decide whether it is asking about study completion timeliness
or filtering studies by their completion classification (On Time, Delayed, Indeterminate).

SUPPORTED QUERY TYPES (set supported = true):
- Studies not completed on time: delayed, overdue, late, missed deadline
- Indeterminate studies: missing planned or actual completion date
- Filter/show studies by classification: "filter studies with classification X",
  "show delayed studies", "show indeterminate studies", "show on time studies"
- Any combination of the above classifications

ALLOWED SERVICES AND FIELDS:
  study-service    → action: listStudies → fields: studyId, studyCode, customer, legalEntity, plannedCompletionDate
  corelabs-service → action: listTestPs  → fields: testpId, studyId, status, completedAt, runType, result

ALLOWED FILTER OPERATORS: =, !=, >, >=, <, <=, in, between, is null, is not null
ALLOWED AGGREGATE FUNCTIONS: max, min, count, sum, avg
MAX ROWS LIMIT: 500

CLASSIFICATION RULES:
- "On Time": actualCompletionDate <= plannedCompletionDate (both dates present)
- "Delayed": actualCompletionDate > plannedCompletionDate (both dates present)
- "Indeterminate": plannedCompletionDate is null OR actualCompletionDate is null

SET output.includeClassifications based on the query intent:
- "show delayed studies" or "not completed on time" or "late" → ["Delayed", "Indeterminate"]
- "show only delayed" or "filter studies with classification Delayed" → ["Delayed"]
- "show only indeterminate" or "filter studies with classification Indeterminate" → ["Indeterminate"]
- "show on time studies" or "filter studies with classification On Time" → ["On Time"]
- "show all studies" or all three classifications requested → ["On Time", "Delayed", "Indeterminate"]

If the query IS about study completion timeliness or classification filtering:
  - Set supported = true
  - Set reason = null
  - Write a clear markdown plan explaining the steps
  - Populate plan with version "1.0", intent "find_studies_not_completed_on_time", the two operations,
    output.includeClassifications set according to the rules above, and limits maxRows 500

If the query is NOT about study completion timeliness:
  - Set supported = false
  - Set reason to a one-sentence explanation
  - Set markdown = null and plan = null
""";

    public OpenAiPlanGenerator(IConfiguration config, ILogger<OpenAiPlanGenerator> logger)
    {
        _logger = logger;
        var apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException(
                "OpenAI:ApiKey is not configured. " +
                "Set via: dotnet user-secrets set \"OpenAI:ApiKey\" \"sk-...\" " +
                "or env var OpenAI__ApiKey");

        _client = new ChatClient("gpt-4o-mini", apiKey);
    }

    public async Task<PlanGeneratorResult> GenerateAsync(string query)
    {
        try
        {
            var completion = await _client.CompleteChatAsync(
                [new SystemChatMessage(SystemPrompt), new UserChatMessage(query)],
                new ChatCompletionOptions
                {
                    // Strict schema: model output is guaranteed to match ResponseSchema.
                    // Eliminates free-form text, markdown fences, missing fields.
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
                _logger.LogError("OpenAI returned supported=true but plan deserialised to null. Raw: {Json}", json);
                return new PlanGeneratorResult(string.Empty, null,
                    "Plan generation service returned an unexpected response.", IsServerError: true);
            }

            return new PlanGeneratorResult(markdown, plan, null);
        }
        catch (JsonException ex)
        {
            // Strict schema mode makes this very unlikely — log for investigation
            _logger.LogError(ex, "OpenAI response failed JSON parse for query: {Query}", query);
            return new PlanGeneratorResult(string.Empty, null,
                "Plan generation service returned an unreadable response.", IsServerError: true);
        }
        catch (Exception ex)
        {
            // Network error, provider outage, auth failure — retryable by the client
            _logger.LogError(ex, "OpenAI call failed for query: {Query}", query);
            return new PlanGeneratorResult(string.Empty, null,
                "Plan generation service is temporarily unavailable.", IsServerError: true);
        }
    }
}
