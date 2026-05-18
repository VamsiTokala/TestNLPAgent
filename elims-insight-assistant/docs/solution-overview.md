# Solution Overview

## Request Flow — What Happens on Every Query

```
1. User types a query in Angular UI
   e.g. "filter studies with classification Indeterminate"
        ↓
2. Angular POSTs to /api/assistant/query via proxy → .NET backend (port 5000)
        ↓
3. IServiceRegistry.GetAll() — builds live contract list from InMemoryServiceRegistry
   (includes any contracts added via UI / POST /api/assistant/contracts)
        ↓
4. IPlanGenerator.GenerateAsync(query)
        ├── GeminiPlanGenerator  (Gemini:ApiKey set) → gemini-2.5-flash, JSON mode
        ├── OpenAiPlanGenerator  (OpenAI:ApiKey set) → gpt-4o-mini, strict JSON schema
        └── MockPlanGenerator    (no key)            → keyword matching, no API call

   The prompt injected into Gemini/OpenAI is built dynamically from the registry:
     • PromptBuilder.ServicesBlock()  — lists every registered contract with action/fields/purpose
     • PromptBuilder.ExampleOperations() — shows the JSON operation shape per service
     • PromptBuilder.CoreInstructions() — classification rules, supported query types

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
5. IPlanValidator.Validate(plan)
   The validator derives its allowlist and required-service list from IServiceRegistry at
   validation time — no hardcoded lists.

   Completeness checks:
     ✓ intent is non-blank
     ✓ operations list is non-empty
     ✓ all registry entries where IsRequired=true are present in operations
     ✓ entities contains "study" and "testp"
   Allowlist checks (derived from registry):
     ✓ every service name matches a registered contract
     ✓ every field is in that contract's field list
     ✓ every filter operator is in the operator allowlist
     ✓ every aggregate function is in the aggregate allowlist
     ✓ no write/update/delete action
     ✓ no SQL/script injection token in filter values
     ✓ maxRows ≤ 500
   → Any failure: HTTP 400, plan is not executed
        ↓
6. IExecutionEngine.ExecuteAsync(plan, userContext)
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
7. IAuditService.Save(record)
   Stores: traceId, planId, userId, query, plan, validation status,
           services called, execution timestamps, summary, result snapshot
   Retrievable via GET /api/assistant/audit/{traceId}
        ↓
8. HTTP 200 → Angular renders:
   ├── AI Interpretation panel
   │     ├── Provider badge (Gemini 2.5 Flash / GPT-4o Mini / Mock)
   │     ├── Intent Detected badge
   │     ├── Classification Filter rows (included / excluded per classification)
   │     ├── Service Contract Selection pipeline with per-operation AI reason
   │     └── Validation checks grid (pass / fail pills)
   ├── Summary stat cards (On Time / Delayed / Indeterminate counts)
   ├── Results table (only when status = "Completed")
   ├── Generated Plan (Markdown)
   └── JSON Execution Plan (collapsible)

   If status = "UnsupportedQuery" → shows reason message, no plan/results rendered.
   If HTTP 4xx/5xx → shows error banner.
```

**The core safety property:** The LLM output is a JSON plan *proposal*. Nothing executes until the
Validator passes every allowlist check. A hallucinated or adversarial plan — wrong service,
write operation, forbidden field — is rejected before any data is read.

---

## Architecture (Component View)

```
User query (Angular UI)
    │
    ▼
IServiceRegistry ────────────────────────────────────────────────────
│  InMemoryServiceRegistry (ConcurrentDictionary)
│  • Seeded with study-service [REQUIRED] and corelabs-service [REQUIRED]
│  • Accepts new contracts at runtime via POST /api/assistant/contracts
│  • Single source of truth for: AI prompt content, validator allowlist,
│    required-service check, and UI contract catalogue
    │
    ▼ (contracts passed into)
IPlanGenerator ──────────────────────────────────────────────────────
│  GeminiPlanGenerator   Gemini:ApiKey set   gemini-2.5-flash JSON mode
│  OpenAiPlanGenerator   OpenAI:ApiKey set   gpt-4o-mini strict schema
│  MockPlanGenerator     no key              keyword matching, no cost
│
│  Prompt is built dynamically: PromptBuilder.CoreInstructions(contracts)
│  includes every registered service with its action, fields, and purpose.
    │
    ▼  ExecutionPlan { version, intent, entities, operations, output, limits }
    │   operations[i].reason — why the AI selected this service
    │
IPlanValidator ──────────────────────────────────────────────────────
│  Plan completeness  →  intent? operations? required services/entities?
│  Service allowlist  →  derived from registry.GetAll() at validation time
│  Field allowlist    →  per-contract field lists from registry
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

## Dynamic Service Registry

The `IServiceRegistry` / `InMemoryServiceRegistry` is the central extension point.
It replaces all hardcoded allowlists and prompt text.

### What it stores per contract

| Field | Purpose |
|---|---|
| `Name` | Unique identifier used in plans (e.g. `study-service`) |
| `DisplayName` | Human-readable label shown in the UI |
| `Action` | The one allowed read action (e.g. `listStudies`) |
| `Fields` | Allowlisted field names for this service |
| `Purpose` | Injected into the AI prompt — tells the model *why* to use this service |
| `Description` | Shown in the UI contract card (defaults to Purpose if blank) |
| `IsRequired` | If true, validator rejects any plan that omits this service |

### What the registry drives automatically

1. **AI prompt** — `PromptBuilder.CoreInstructions()` lists every contract so the LLM knows what's available.
2. **Validator allowlist** — `PlanValidator` calls `registry.GetAll()` at validation time; no restarts required.
3. **Required-service check** — contracts with `IsRequired=true` must appear in every valid plan.
4. **UI catalogue** — `GET /api/assistant/contracts` returns the live list; contract cards update immediately.

### REST API

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/assistant/contracts` | Returns all registered service contracts |
| `POST` | `/api/assistant/contracts` | Registers a new contract; returns updated list |
| `POST` | `/api/assistant/query` | Runs a natural-language query |
| `GET` | `/api/assistant/audit/{traceId}` | Retrieves an audit record |

### Register a new contract via UI (zero code changes)

1. Open the app and click **+ Register Contract** in the Service Catalogue panel.
2. Fill in: Service Name, Display Name, Action, Fields (comma-separated), and Purpose (injected into the AI prompt).
3. Click **Register Contract**.

The contract is stored in the registry. The next query automatically:
- Includes the new service in the AI prompt
- Allows the new service name/fields in the validator
- Shows the contract card in the UI with "Selected" / "Unselected" state

> **Limitation:** `InMemoryServiceRegistry` is process-local. Contracts registered at runtime are
> lost when the backend restarts. For production, back the registry with a database and implement
> `IServiceRegistry` over a persistent store.

---

## Extending the Design — Adding a New Service Contract

### Option A: Runtime via UI (no code changes)

Register the contract through the **+ Register Contract** form in the UI. The AI will begin
considering it on the next query. See "Register a new contract via UI" above.

Use this for demos, prototyping, and non-critical optional services.

### Option B: Compile-time (persisted across restarts)

Seed the contract in `InMemoryServiceRegistry` alongside the two built-in entries:

```csharp
// Services/ServiceRegistry.cs — add inside InMemoryServiceRegistry constructor
_contracts["sample-service"] = new ServiceContractEntry(
    Name:        "sample-service",
    DisplayName: "Sample Service",
    Action:      "listSamples",
    Fields:      ["sampleId", "studyId", "status", "collectedAt"],
    Purpose:     "Provides sample collection records and collection timestamps",
    Description: "Sample collection catalogue — timestamps, status, and study linkage",
    IsRequired:  false   // optional: set true to mandate it in every plan
);
```

Then add the data client and wire up `ExecutionEngine`:

**Step 1 — Define the service client** (`Services/NewDataServices.cs`):

```csharp
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

**Step 2 — Register in DI** (`Program.cs`):

```csharp
builder.Services.AddScoped<ISampleServiceClient, DemoSampleServiceClient>();
```

**Step 3 — Use in ExecutionEngine** (`Execution/ExecutionEngine.cs`):

```csharp
public class ExecutionEngine(
    IStudyServiceClient studyClient,
    ICoreLabsServiceClient coreLabsClient,
    ISampleServiceClient sampleClient,   // ← inject
    IClassificationService classificationService) : IExecutionEngine
{
    public async Task<...> ExecuteAsync(ExecutionPlan plan, UserContext userContext)
    {
        // existing fetch + classify logic ...

        if (plan.Operations.Any(o => o.Service == "sample-service"))
        {
            var samples = await sampleClient.ListSamplesAsync();
            // join, enrich, or aggregate as needed
        }
    }
}
```

No changes needed to `PlanGenerator.cs`, `PlanValidator.cs`, or any frontend files —
the registry handles all of that automatically.

### Summary checklist (Option B)

| # | File | What to add |
|---|---|---|
| 1 | `Services/ServiceRegistry.cs` | Seed entry in `InMemoryServiceRegistry` constructor |
| 2 | `Services/NewDataServices.cs` | Interface + demo client + DTO |
| 3 | `seed-data/<service>.json` | Seed data file |
| 4 | `Program.cs` | DI registration for the client |
| 5 | `Execution/ExecutionEngine.cs` | Inject client + use in `ExecuteAsync` |

---

## Plan Generator — Three Modes

| Mode | Class | Activated when | How It Works |
|---|---|---|---|
| **Gemini** (recommended) | `GeminiPlanGenerator` | `Gemini:ApiKey` is set | Sends query + dynamic prompt to gemini-2.5-flash with `ResponseMimeType=application/json`; parses response into `ExecutionPlan`; post-parse check rejects blank intent or empty operations |
| **OpenAI** | `OpenAiPlanGenerator` | `OpenAI:ApiKey` is set, no Gemini key | Sends query + system prompt to gpt-4o-mini with strict JSON schema enforcement; schema includes `output.includeClassifications` and per-operation `reason` |
| **Mock** | `MockPlanGenerator` | No API key (local dev, CI, tests) | Lowercases query, matches phrase list, calls `ResolveClassifications()` to pick the right filter — no network call, no cost |

**Priority order:** Gemini → OpenAI → Mock.

All three generators receive the live registry so their output always reflects
the current set of registered contracts.

---

## AI Interpretation Panel — What the UI Shows

After a query runs, the AI Interpretation panel reveals the LLM's reasoning in real time:

| Section | What it shows |
|---|---|
| **Provider badge** | Which generator was used (Gemini 2.5 Flash / GPT-4o Mini / Mock) |
| **Intent Detected** | The `intent` string from the plan (e.g. `find_studies_not_completed_on_time`) |
| **Classification Filter** | All three classifications with "included" / "excluded" status based on `output.includeClassifications` |
| **Service Contract Selection** | Each selected operation as a pipeline card showing service name, action tag, and the AI's per-operation `reason` |
| **Validation** | Pass/fail pill for every check the validator ran |

The Service Catalogue cards simultaneously show "✓ Selected" on contracts the AI picked
and dim the ones it skipped, with the AI's reason displayed inline.

---

## Error Handling Contract

| Scenario | HTTP | `status` field | Client should |
|---|---|---|---|
| Query out of scope | 200 | `UnsupportedQuery` | Show reason message, do not retry |
| Plan fails validation | 400 | — | Fix the plan |
| Gemini returns incomplete plan | 200 | `UnsupportedQuery` | Show reason message, do not retry |
| LLM provider down / network error | 503 | — | Retry with exponential backoff |
| Success | 200 | `Completed` | Display summary + results |

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
