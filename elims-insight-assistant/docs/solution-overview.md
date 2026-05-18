# Solution Overview

## Architecture

```
User query
    ↓
IPlanGenerator (async)
    ├── GeminiPlanGenerator  — when Gemini:ApiKey is configured (gemini-2.5-flash, JSON mode) [recommended]
    ├── OpenAiPlanGenerator  — when OpenAI:ApiKey is configured (gpt-4o-mini, strict JSON schema)
    └── MockPlanGenerator    — fallback when no key is set (keyword matching, no API call)
    ↓
IPlanValidator
    — allowlist check: services, fields, operators, aggregates, maxRows
    — injection detection on filter values
    ↓
IExecutionEngine
    — authorisation (role check)
    — legal entity filter
    — fetch studies + TestPs from demo seed data
    — correlate, group, derive actual completion (max TestP timestamp)
    — classify: On Time / Delayed / Indeterminate
    ↓
IAuditService
    — persist full query record in ConcurrentDictionary (in-memory)
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

**Getting a free key (Gemini):** Go to https://aistudio.google.com → Get API key. No billing required. Hundreds of free queries per day.

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
