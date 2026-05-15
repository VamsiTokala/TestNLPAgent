using System.Text.Json;
using ElimsInsightAssistant.Api.Models;
using OpenAI.Chat;

namespace ElimsInsightAssistant.Api.Services;

public interface IPlanGenerator
{
    Task<(string markdown, ExecutionPlan? plan, string? error)> GenerateAsync(string query);
}

// ─── Mock (no API key required — used for local dev and tests) ───────────────

public class MockPlanGenerator : IPlanGenerator
{
    private static readonly string[] SupportedTerms =
        ["not completed on time", "delayed studies", "completed late", "not on time", "indeterminate"];

    public Task<(string markdown, ExecutionPlan? plan, string? error)> GenerateAsync(string query)
    {
        var q = query.ToLowerInvariant();
        if (!SupportedTerms.Any(q.Contains))
            return Task.FromResult((string.Empty, (ExecutionPlan?)null,
                (string?)"This demo currently supports queries related to study completion timeliness."));

        var markdown = """
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
8. Return Delayed and Indeterminate studies with supporting details.

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
            ]
        };

        return Task.FromResult((markdown, (ExecutionPlan?)plan, (string?)null));
    }
}

// ─── OpenAI (real NL intent extraction) ──────────────────────────────────────

public class OpenAiPlanGenerator : IPlanGenerator
{
    private readonly ChatClient _client;

    // System prompt teaches the model the exact JSON shape we expect back.
    // The validator is the safety net — but we also constrain the model to
    // only the allowed services/fields so it produces valid plans by default.
    private const string SystemPrompt = """
You are a governed analytics plan generator for a laboratory information management system (LIMS).

Given a natural language query, decide whether it is asking about study completion timeliness
(delayed studies, not completed on time, late, overdue, indeterminate completion, etc.).

ALLOWED SERVICES AND FIELDS:
  study-service    → action: listStudies  → fields: studyId, studyCode, customer, legalEntity, plannedCompletionDate
  corelabs-service → action: listTestPs  → fields: testpId, studyId, status, completedAt, runType, result

ALLOWED FILTER OPERATORS: =, !=, >, >=, <, <=, in, between, is null, is not null
ALLOWED AGGREGATE FUNCTIONS: max, min, count, sum, avg
MAX ROWS LIMIT: 500

If the query IS about study completion timeliness, respond with this JSON (no markdown fences, no extra text):
{
  "supported": true,
  "markdown": "# Analysis Plan\nIntent: <one line intent>\n\n## Steps\n1. ...\n\n## Execution mode\nRead-only, deterministic, approved service contracts only.",
  "plan": {
    "version": "1.0",
    "intent": "find_studies_not_completed_on_time",
    "entities": ["study", "testp"],
    "operations": [
      {
        "service": "study-service",
        "action": "listStudies",
        "select": ["studyId", "studyCode", "customer", "legalEntity", "plannedCompletionDate"],
        "filters": []
      },
      {
        "service": "corelabs-service",
        "action": "listTestPs",
        "select": ["testpId", "studyId", "status", "completedAt", "runType", "result"],
        "filters": [{ "field": "status", "op": "=", "value": "Completed" }]
      }
    ],
    "correlate": { "leftEntity": "study", "rightEntity": "testp", "leftField": "studyId", "rightField": "studyId" },
    "transform": {
      "groupBy": ["studyId"],
      "aggregates": [{ "field": "completedAt", "fn": "max", "as": "actualCompletionDate" }]
    },
    "classify": {
      "onTime": "actualCompletionDate <= plannedCompletionDate",
      "delayed": "actualCompletionDate > plannedCompletionDate",
      "indeterminate": "plannedCompletionDate is null OR actualCompletionDate is null"
    },
    "output": {
      "includeClassifications": ["Delayed", "Indeterminate"],
      "columns": ["studyId", "studyCode", "customer", "plannedCompletionDate", "actualCompletionDate", "classification", "reason", "dataQualityFlags"]
    },
    "limits": { "maxRows": 500, "pagination": true }
  }
}

If the query is NOT about study completion timeliness, respond with:
{ "supported": false, "reason": "<one sentence explanation>" }

Return ONLY valid JSON. No markdown fences. No extra text before or after.
""";

    public OpenAiPlanGenerator(IConfiguration config)
    {
        var apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException(
                "OpenAI:ApiKey is not configured. Set it via environment variable OpenAI__ApiKey " +
                "or dotnet user-secrets set \"OpenAI:ApiKey\" \"sk-...\"");

        // gpt-4o-mini: fast, cheap, reliable JSON output — good fit for structured plan generation
        _client = new ChatClient("gpt-4o-mini", apiKey);
    }

    public async Task<(string markdown, ExecutionPlan? plan, string? error)> GenerateAsync(string query)
    {
        try
        {
            var completion = await _client.CompleteChatAsync(
                [
                    new SystemChatMessage(SystemPrompt),
                    new UserChatMessage(query)
                ],
                new ChatCompletionOptions
                {
                    ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
                });

            var json = completion.Value.Content[0].Text;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.GetProperty("supported").GetBoolean())
            {
                var reason = root.TryGetProperty("reason", out var r)
                    ? r.GetString()
                    : "Query not supported by this assistant.";
                return (string.Empty, null, reason);
            }

            var markdown = root.GetProperty("markdown").GetString() ?? string.Empty;
            var planJson = root.GetProperty("plan").GetRawText();
            var plan = JsonSerializer.Deserialize<ExecutionPlan>(planJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return plan is null
                ? (string.Empty, null, "Failed to parse execution plan from AI response.")
                : (markdown, plan, null);
        }
        catch (JsonException ex)
        {
            return (string.Empty, null, $"AI returned invalid JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (string.Empty, null, $"AI plan generation failed: {ex.Message}");
        }
    }
}
