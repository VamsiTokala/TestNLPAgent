# eLIMS Insight Assistant: Governed Natural-Language Analytics

A governed NL analytics assistant for a laboratory information management system (LIMS).
Users type plain-English questions; the system generates a validated, audited execution plan
and returns structured results — without letting an AI freely query databases.

## How It Works

```
User query → Plan Generator → Validator → Execution Engine → Audit → Response
```

Every query is validated against an allowlist before anything runs. Every query is recorded.

## Plan Generator Modes

| Mode | Activated when | Behaviour |
|---|---|---|
| **OpenAI** (`gpt-4o-mini`) | `OpenAI:ApiKey` is configured | Real NL intent extraction — understands paraphrases like "overdue trials", "missed deadline" |
| **Mock** (keyword match) | No API key (default) | Matches hardcoded phrases — suitable for local dev and tests without an API key |

## Quick Start

### 1. Backend API
```bash
cd elims-insight-assistant/backend/src/ElimsInsightAssistant.Api
dotnet run --urls http://localhost:5000
```
Swagger UI: http://localhost:5000/swagger

### 2. Add OpenAI Key (optional — skip for mock mode)
```bash
dotnet user-secrets set "OpenAI:ApiKey" "sk-proj-..."
```

### 3. Run Tests
```bash
cd elims-insight-assistant/backend/src/ElimsInsightAssistant.Tests
dotnet test
```

### 4. Frontend
```bash
cd elims-insight-assistant/frontend/elims-insight-assistant-ui
ng serve
```

## Try It — Example Request

```bash
curl -X POST http://localhost:5000/api/assistant/query \
  -H "Content-Type: application/json" \
  -d '{
    "query": "show me studies not completed on time",
    "userContext": {
      "userId": "user1",
      "roles": ["StudyViewer", "CoreLabsViewer"],
      "legalEntities": ["EU"]
    }
  }'
```

Expected: ST-002 (Delayed, 2 days late) and ST-004 (Indeterminate, missing data).

## API Endpoints

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/assistant/query` | Full pipeline: generate → validate → execute → audit |
| POST | `/api/assistant/plan` | Generate plan only, no execution |
| POST | `/api/assistant/plan/validate` | Validate a plan without executing |
| POST | `/api/assistant/execute` | Execute a pre-built validated plan |
| GET | `/api/assistant/audit/{traceId}` | Retrieve audit record by trace ID |
| GET | `/api/demo/studies` | View seed study data |
| GET | `/api/demo/corelabs/testps` | View seed TestP data |

## Error Handling

| HTTP Status | Meaning | Client should... |
|---|---|---|
| 200 `status: Completed` | Success | Display results |
| 200 `status: UnsupportedQuery` | Query out of scope | Show message to user, do not retry |
| 400 | Plan failed allowlist validation | Fix the plan |
| 503 `status: ServiceUnavailable` | OpenAI unavailable (network/outage) | Retry with backoff |

Raw exception details are **never** in HTTP responses — they are logged server-side only.

## Project Structure

```
elims-insight-assistant/
├── backend/src/
│   ├── ElimsInsightAssistant.Api/
│   │   ├── Controllers/     HTTP entry points
│   │   ├── Services/        Plan generation (Mock + OpenAI), classification, data clients
│   │   ├── Validation/      Allowlist validator
│   │   ├── Execution/       Execution engine
│   │   ├── Audit/           In-memory audit store
│   │   └── Models/          All data shapes
│   └── ElimsInsightAssistant.Tests/
├── frontend/elims-insight-assistant-ui/   Angular UI scaffold
├── seed-data/                             studies.json, testps.json
└── docs/
    ├── build-from-scratch.md              Full beginner's guide
    ├── solution-overview.md               Architecture summary
    ├── validation-rules.md                Allowlist rules reference
    ├── service-contracts.md               Service contract definitions
    ├── sample-queries.md                  Example queries to try
    └── demo-script.md                     Demo walkthrough script
```

## Why the AI Does Not Execute Directly

OpenAI (or any LLM) only proposes a JSON plan. Before any data is fetched:
- The plan is checked against a strict allowlist (services, fields, operators)
- The user's roles and legal entities are verified
- Write operations are unconditionally blocked

This makes the system safe for regulated environments regardless of what the LLM returns.
