# Solution Overview

## Request Flow — What Happens on Every Query

```
1. User types a query in Angular UI
   e.g. "filter studies with classification Indeterminate"
        ↓
2. Angular POSTs to /api/assistant/query via proxy → .NET backend (port 5000)
        ↓
3. IPlanGenerator.GenerateAsync(query)
        ├── GeminiPlanGenerator  (Gemini:ApiKey set) → gemini-2.5-flash, JSON mode
        ├── OpenAiPlanGenerator  (OpenAI:ApiKey set) → gpt-4o-mini, strict JSON schema
        └── MockPlanGenerator    (no key)            → keyword matching, no API call

   Output: PlanGeneratorResult { Markdown, ExecutionPlan?, IsServerError }

   Classification mapping (all three generators apply the same rules):
     "not completed on time / delayed / late"  → includeClassifications: ["Delayed","Indeterminate"]
     "show only delayed"                        → includeClassifications: ["Delayed"]
     "show only indeterminate"                  → includeClassifications: ["Indeterminate"]
     "filter studies with classification X"     → includeClassifications: ["X"]
     "show on time studies"                     → includeClassifications: ["On Time"]
     "show all studies"                         → includeClassifications: ["On Time","Delayed","Indeterminate"]

   If Gemini returns supported=true but intent or operations are empty,
   GeminiPlanGenerator treats it as UnsupportedQuery (not a server error).
        ↓
4. IPlanValidator.Validate(plan)
   Completeness checks (new):
     ✓ intent is non-blank
     ✓ operations list is non-empty
     ✓ study-service is present in operations
     ✓ corelabs-service is present in operations
     ✓ entities contains "study" and "testp"
   Allowlist checks:
     ✓ every service name is in the allowlist
     ✓ every field is in that service's field allowlist
     ✓ every filter operator is in the operator allowlist
     ✓ every aggregate function is in the aggregate allowlist
     ✓ no write/update/delete action
     ✓ no SQL/script injection token in filter values
     ✓ maxRows ≤ 500
   → Any failure: HTTP 400, plan is not executed
        ↓
5. IExecutionEngine.ExecuteAsync(plan, userContext)
   ✓ verifies user roles: StudyViewer + CoreLabsViewer (both required)
   ✓ fetches studies, filters to user's permitted legalEntities
   ✓ fetches completed TestPs (Status="Completed", CompletedAt not null)
   ✓ correlates by studyId, derives actualCompletionDate = max(TestP.completedAt)
   ✓ classifies each study:
       On Time       → actualCompletionDate ≤ plannedCompletionDate (both present)
       Delayed       → actualCompletionDate > plannedCompletionDate (both present)
       Indeterminate → either date is null
   ✓ builds summary counts across ALL studies (before filter)
   ✓ filters rows by plan.Output.IncludeClassifications
   ✓ truncates to plan.Limits.MaxRows (≤ 500)
        ↓
6. IAuditService.Save(record)
   Stores: traceId, planId, userId, query, plan, validation status,
           services called, execution timestamps, summary, result snapshot
   Retrievable via GET /api/assistant/audit/{traceId}
        ↓
7. HTTP 200 → Angular renders:
   ├── Summary cards (On Time / Delayed / Indeterminate counts)
   ├── Results table (only when status = "Completed")
   ├── Generated Plan (Markdown)
   └── JSON Execution Plan (collapsible)

   If status = "UnsupportedQuery" → shows reason message, no plan/results rendered.
   If HTTP 4xx/5xx → shows error banner.
```

**The core safety property:** The LLM output is a JSON plan *proposal*. Nothing executes until the
Validator passes every allowlist check at step 4. A hallucinated or adversarial plan — wrong service,
write operation, forbidden field — is rejected before any data is read.

---

## Architecture (Component View)

```
User query (Angular UI)
    │
    ▼
IPlanGenerator ──────────────────────────────────────────────────────
│  GeminiPlanGenerator   Gemini:ApiKey set   gemini-2.5-flash JSON mode
│  OpenAiPlanGenerator   OpenAI:ApiKey set   gpt-4o-mini strict schema
│  MockPlanGenerator     no key              keyword matching, no cost
    │
    ▼  ExecutionPlan { intent, entities, operations, output, limits }
    │
IPlanValidator ──────────────────────────────────────────────────────
│  Plan completeness  →  intent? operations? required services/entities?
│  Service allowlist  →  only study-service, corelabs-service
│  Field allowlist    →  per-service field lists
│  Operator allowlist →  =, !=, >, >=, <, <=, in, between, is null, is not null
│  Aggregate allowlist→  max, min, count, sum, avg
│  Write guard        →  update/delete/write unconditionally rejected
│  Injection guard    →  SQL/script tokens rejected in filter values
│  Row limit          →  maxRows ≤ 500
    │
    ▼  ValidationResult { Status, Checks[], Errors[] }
    │
IExecutionEngine ────────────────────────────────────────────────────
│  Role + legal-entity authorisation
│  Fetch seed data (studies.json, testps.json)
│  Correlate → classify → filter by includeClassifications
    │
    ▼  (QuerySummary, List<StudyCompletionResult>, servicesCalled[])
    │
IAuditService ───────────────────────────────────────────────────────
│  In-memory ConcurrentDictionary, queryable by traceId
    │
    ▼
HTTP 200 JSON response
```

---

## Plan Generator — Three Modes

| Mode | Class | Activated when | How It Works |
|---|---|---|---|
| **Gemini** (recommended) | `GeminiPlanGenerator` | `Gemini:ApiKey` is set | Sends query + structured prompt to gemini-2.5-flash with `ResponseMimeType=application/json`; parses response into `ExecutionPlan`; post-parse check rejects blank intent or empty operations |
| **OpenAI** | `OpenAiPlanGenerator` | `OpenAI:ApiKey` is set, no Gemini key | Sends query + system prompt to gpt-4o-mini with strict JSON schema enforcement; schema includes `output.includeClassifications` |
| **Mock** | `MockPlanGenerator` | No API key (local dev, CI, tests) | Lowercases query, matches phrase list, calls `ResolveClassifications()` to pick the right filter — no network call, no cost |

**Priority order:** Gemini → OpenAI → Mock.

---

## Error Handling Contract

| Scenario | HTTP | `status` field | Client should |
|---|---|---|---|
| Query out of scope | 200 | `UnsupportedQuery` | Show reason message, do not retry |
| Plan fails validation | 400 | — | Fix the plan |
| Gemini returns incomplete plan | 200 | `UnsupportedQuery` | Show reason message, do not retry |
| LLM provider down / network error | 503 | `ServiceUnavailable` | Retry with exponential backoff |
| Success | 200 | `Completed` | Display summary + results |

---

## Extending the Design — Adding a New Service Contract

This section explains what to change when you want the assistant to query a new
downstream service (e.g. a `sample-service`, `equipment-service`, etc.).

### Step 1 — Define the service interface and demo client

```csharp
// Services/NewDataServices.cs
public interface ISampleServiceClient
{
    Task<List<SampleDto>> ListSamplesAsync();
}

public class DemoSampleServiceClient(IWebHostEnvironment env) : ISampleServiceClient
{
    public async Task<List<SampleDto>> ListSamplesAsync()
    {
        var file = Path.Combine(env.ContentRootPath, "..", "..", "..", "seed-data", "samples.json");
        var json = await File.ReadAllTextAsync(Path.GetFullPath(file));
        return JsonSerializer.Deserialize<List<SampleDto>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }
}

public record SampleDto(string SampleId, string StudyId, string Status, DateTime? CollectedAt);
```

Add seed data to `seed-data/samples.json`.

### Step 2 — Register in DI (`Program.cs`)

```csharp
builder.Services.AddScoped<ISampleServiceClient, DemoSampleServiceClient>();
```

### Step 3 — Add to the validator allowlist (`PlanValidator.cs`)

```csharp
private static readonly Dictionary<string, (HashSet<string> actions, HashSet<string> fields)> Allowlist = new()
{
    ["study-service"]    = ( ["listStudies"],  ["studyId","studyCode","customer","legalEntity","plannedCompletionDate"] ),
    ["corelabs-service"] = ( ["listTestPs"],   ["testpId","studyId","status","completedAt","runType","result"] ),
    // ── new ──────────────────────────────────────────────────────────────
    ["sample-service"]   = ( ["listSamples"],  ["sampleId","studyId","status","collectedAt"] ),
};
```

Also add `"sample-service"` to `RequiredServices` if you want the validator to
mandate it, or leave it optional and only check presence when the plan asks for it.

### Step 4 — Update the prompts (`PlanGenerator.cs`)

In **both** `GeminiPlanGenerator.FullPrompt` and `OpenAiPlanGenerator.SystemPrompt`, add the new service to the `ALLOWED SERVICES AND FIELDS` block:

```
ALLOWED SERVICES AND FIELDS:
  study-service    → action: listStudies  → fields: studyId, studyCode, customer, legalEntity, plannedCompletionDate
  corelabs-service → action: listTestPs   → fields: testpId, studyId, status, completedAt, runType, result
  sample-service   → action: listSamples  → fields: sampleId, studyId, status, collectedAt   ← new
```

For the **OpenAI strict schema**, add the new service name/action to the `operations.items` schema if you want it schema-enforced.

For the **Mock generator**, add matching keywords to `SupportedTerms` and add a `ResolveClassifications`-style helper if the new service requires its own filter logic.

### Step 5 — Use the new service in `ExecutionEngine.cs`

```csharp
public class ExecutionEngine(
    IStudyServiceClient studyClient,
    ICoreLabsServiceClient coreLabsClient,
    ISampleServiceClient sampleClient,          // ← inject new client
    IClassificationService classificationService) : IExecutionEngine
{
    public async Task<...> ExecuteAsync(ExecutionPlan plan, UserContext userContext)
    {
        // existing fetch + classify logic ...

        // fetch samples only when the plan asks for it
        if (plan.Operations.Any(o => o.Service == "sample-service"))
        {
            var samples = await sampleClient.ListSamplesAsync();
            // join, enrich, or aggregate as needed
        }
    }
}
```

### Step 6 — (Optional) Add the new role to `UserContext`

If the new service requires a separate authorisation role, add it to the
role check at the top of `ExecutionEngine.ExecuteAsync` and update the demo
`userContext` in `InsightAssistantApiService` on the frontend.

### Summary checklist

| # | File | What to add |
|---|---|---|
| 1 | `Services/NewDataServices.cs` | Interface + demo client + DTO |
| 2 | `seed-data/<service>.json` | Seed data file |
| 3 | `Program.cs` | DI registration |
| 4 | `Validation/PlanValidator.cs` | Allowlist entry |
| 5 | `Services/PlanGenerator.cs` | Prompt text (Gemini + OpenAI + Mock) |
| 6 | `Execution/ExecutionEngine.cs` | Inject client + use in ExecuteAsync |
| 7 | Frontend model | Add new fields to TypeScript interfaces if needed |

---

## Seed-Data Expected Output

| Study | Customer | Entity | Planned | Actual | Classification |
|---|---|---|---|---|---|
| ST-001 | ABC Pharma | EU | Apr 10 | Apr 9 | On Time |
| ST-002 | XYZ Labs | EU | Apr 15 | Apr 17 | **Delayed** |
| ST-003 | BioTest | US | Apr 20 | Apr 18 | On Time |
| ST-004 | Delta Bio | EU | null | null | **Indeterminate** |

Default query (legalEntities: EU + US, includeClassifications: Delayed + Indeterminate)
returns ST-002 and ST-004. Summary always shows all four studies.
