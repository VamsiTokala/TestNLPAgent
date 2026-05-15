# Solution Overview

## Architecture

```
User query
    ↓
IPlanGenerator (async)
    ├── OpenAiPlanGenerator  — when OpenAI:ApiKey is configured (gpt-4o-mini, strict JSON schema)
    └── MockPlanGenerator    — fallback for local dev / tests (keyword matching)
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

## Plan Generator — Two Modes

| Mode | Class | When Used | How It Works |
|---|---|---|---|
| **OpenAI** | `OpenAiPlanGenerator` | `OpenAI:ApiKey` is set | Sends query + system prompt to gpt-4o-mini with strict JSON schema; parses schema-constrained response into `ExecutionPlan` |
| **Mock** | `MockPlanGenerator` | No API key (local dev, tests) | Lowercases query, checks hardcoded phrase list, returns fixed plan |

Switching is automatic — `Program.cs` reads config at startup and registers the right implementation.

## Why LLM Does Not Execute Directly

The plan generator only proposes a plan. Execution only happens after:
1. The plan passes the validator allowlist
2. The user has the required roles (`StudyViewer`, `CoreLabsViewer`)
3. Results are filtered to the user's permitted legal entities

This means OpenAI's output — however creative — cannot bypass the safety net.

## Error Handling Contract

| Scenario | HTTP Status | Meaning |
|---|---|---|
| Query understood, not in scope | 200 `UnsupportedQuery` | Client should not retry — query is genuinely unsupported |
| OpenAI down / network error | 503 `ServiceUnavailable` | Client should retry — transient failure |
| Plan fails validation | 400 `BadRequest` | Plan produced by OpenAI failed the allowlist check |
| User lacks roles | 500 (unhandled) | Authorization failed before execution |

`PlanGeneratorResult.IsServerError` distinguishes the first two cases in code.
Raw exception details are always logged server-side and never exposed to API consumers.

## Seed-Data Expected Output

| Study | Customer | Entity | Planned | Actual | Classification |
|---|---|---|---|---|---|
| ST-001 | ABC Pharma | EU | Apr 10 | Apr 9 | On Time |
| ST-002 | XYZ Labs | EU | Apr 15 | Apr 17 | **Delayed** |
| ST-003 | BioTest | US | Apr 20 | Apr 18 | On Time |
| ST-004 | Delta Bio | EU | null | null | **Indeterminate** |

Default query (EU legal entity) returns ST-002 and ST-004.
