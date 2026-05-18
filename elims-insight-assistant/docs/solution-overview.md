# Solution Overview

## Request Flow — What Happens on Every Query

```
1. User types a query in Angular UI  (e.g. "which studies missed their deadline?")
        ↓
2. Angular POSTs to /api/assistant/query via proxy → .NET backend on port 5000
        ↓
3. IPlanGenerator.GenerateAsync(query)
        ├── GeminiPlanGenerator  (Gemini:ApiKey set) → calls gemini-2.5-flash → returns JSON plan
        ├── OpenAiPlanGenerator  (OpenAI:ApiKey set) → calls gpt-4o-mini  → returns JSON plan
        └── MockPlanGenerator    (no key)            → keyword match      → returns fixed plan
   Output: PlanGeneratorResult { Markdown, ExecutionPlan, IsServerError }
        ↓
4. IPlanValidator.Validate(plan)
   — checks every service name, field, operator, aggregate against allowlist
   — rejects write operations unconditionally
   — if any check fails → HTTP 400, plan is not executed
        ↓
5. IExecutionEngine.ExecuteAsync(plan, userContext)
   — verifies user has required roles (StudyViewer, CoreLabsViewer)
   — filters results to user's permitted legal entities
   — fetches studies from DemoStudyServiceClient (seed JSON)
   — fetches TestPs from DemoCoreLabsServiceClient (seed JSON)
   — correlates by studyId, derives actualCompletionDate = max(TestP.completedAt)
   — classifies each study: On Time / Delayed / Indeterminate
        ↓
6. IAuditService.Save(record)
   — stores traceId, planId, userId, query, plan, validation, results, timestamps
   — retrievable via GET /api/assistant/audit/{traceId}
        ↓
7. HTTP 200 response → Angular renders Summary + Results + Plan + JSON Plan
```

**Why the LLM never touches data directly:**
The LLM output is a JSON plan proposal — nothing executes until the Validator approves it.
A malicious or hallucinated plan (wrong service, write operation, forbidden field) is rejected
at step 4 before any data is fetched. This makes the system safe regardless of LLM behaviour.

## Architecture (Component View)

```
User query
    ↓
IPlanGenerator (async)
    ├── GeminiPlanGenerator  — Gemini:ApiKey configured (gemini-2.5-flash, JSON mode) [recommended]
    ├── OpenAiPlanGenerator  — OpenAI:ApiKey configured (gpt-4o-mini, strict JSON schema)
    └── MockPlanGenerator    — no key set (keyword matching, no API call, no cost)
    ↓
IPlanValidator
    — allowlist: services, fields, operators, aggregates, maxRows
    — injection detection on filter values
    ↓
IExecutionEngine
    — role + legal entity authorisation
    — fetch seed data, correlate, classify
    ↓
IAuditService
    — in-memory ConcurrentDictionary, queryable by traceId
    ↓
HTTP response (JSON)
```

## Plan Generator — Three Modes

| Mode | Class | Activated when | How It Works |
|---|---|---|---|
| **Gemini** (recommended) | `GeminiPlanGenerator` | `Gemini:ApiKey` is set | Sends query + prompt to gemini-2.5-flash with JSON mode (`ResponseMimeType = application/json`); parses JSON response into `ExecutionPlan` |
| **OpenAI** | `OpenAiPlanGenerator` | `OpenAI:ApiKey` is set, no Gemini key | Sends query + system prompt to gpt-4o-mini with strict JSON schema; parses schema-constrained response into `ExecutionPlan` |
| **Mock** | `MockPlanGenerator` | No API key (local dev, CI, tests) | Lowercases query, checks hardcoded phrase list, returns fixed plan — no network call |

**Priority order:** Gemini → OpenAI → Mock. Switching is automatic — `Program.cs` reads config at startup and registers the right implementation. The startup log always states which mode is active.

**Getting a free key (Gemini):** Go to https://aistudio.google.com → Get API key → Create API key in new project. No billing required.
Free tier limits: **5 requests/minute, 20 requests/day** for `gemini-2.5-flash`. Use that model specifically — older names (gemini-2.0-flash, gemini-1.5-flash) have zero quota on new projects.

**User Secrets require Development environment:** Run with `ASPNETCORE_ENVIRONMENT=Development` (or set `$env:Gemini__ApiKey` directly as an env var, which works in any environment).

## Why the LLM Does Not Execute Directly

The plan generator only proposes a plan. Execution only happens after:
1. The plan passes the validator allowlist (services, fields, operators, row limits)
2. The user has the required roles (`StudyViewer`, `CoreLabsViewer`)
3. Results are filtered to the user's permitted legal entities

This means the LLM's output — however creative — cannot bypass the safety net.
Write operations are unconditionally blocked regardless of what the LLM returns.

## Error Handling Contract

| Scenario | HTTP Status | `status` field | Client should... |
|---|---|---|---|
| Query out of scope | 200 | `UnsupportedQuery` | Show message — do not retry |
| LLM provider down / network error | 503 | `ServiceUnavailable` | Retry with backoff — transient failure |
| Plan fails allowlist validation | 400 | — | Fix the plan |
| Success | 200 | `Completed` | Display results |

`PlanGeneratorResult.IsServerError` distinguishes transient provider failures from unsupported queries in code.
Raw exception details are always logged server-side and never exposed to API consumers.

## Seed-Data Expected Output

| Study | Customer | Entity | Planned | Actual | Classification |
|---|---|---|---|---|---|
| ST-001 | ABC Pharma | EU | Apr 10 | Apr 9 | On Time |
| ST-002 | XYZ Labs | EU | Apr 15 | Apr 17 | **Delayed** |
| ST-003 | BioTest | US | Apr 20 | Apr 18 | On Time |
| ST-004 | Delta Bio | EU | null | null | **Indeterminate** |

Default query (EU legal entity) returns ST-002 and ST-004.
