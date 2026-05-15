# Solution Overview

## Architecture

```
User query
    ↓
IPlanGenerator (async)
    ├── OpenAiPlanGenerator  — when OpenAI:ApiKey is configured (gpt-4o-mini, JSON mode)
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
| **OpenAI** | `OpenAiPlanGenerator` | `OpenAI:ApiKey` is set | Sends query + system prompt to gpt-4o-mini, parses JSON response into `ExecutionPlan` |
| **Mock** | `MockPlanGenerator` | No API key (local dev, tests) | Lowercases query, checks hardcoded phrase list, returns fixed plan |

Switching is automatic — `Program.cs` reads config at startup and registers the right implementation.

## Why LLM Does Not Execute Directly

The plan generator only proposes a plan. Execution only happens after:
1. The plan passes the validator allowlist
2. The user has the required roles (`StudyViewer`, `CoreLabsViewer`)
3. Results are filtered to the user's permitted legal entities

This means OpenAI's output — however creative — cannot bypass the safety net.

## Seed-Data Expected Output

| Study | Customer | Entity | Planned | Actual | Classification |
|---|---|---|---|---|---|
| ST-001 | ABC Pharma | EU | Apr 10 | Apr 9 | On Time |
| ST-002 | XYZ Labs | EU | Apr 15 | Apr 17 | **Delayed** |
| ST-003 | BioTest | US | Apr 20 | Apr 18 | On Time |
| ST-004 | Delta Bio | EU | null | null | **Indeterminate** |

Default query (EU legal entity) returns ST-002 and ST-004.
