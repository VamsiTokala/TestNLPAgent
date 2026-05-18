# eLIMS Insight Assistant: Governed Natural-Language Analytics

A governed NL analytics assistant for a laboratory information management system (LIMS).
Users type plain-English questions; the system generates a validated, audited execution plan
and returns structured results — without letting an AI freely query databases.

## How It Works

```
User types query in Angular UI
        ↓
Angular dev server proxies POST /api/assistant/query to .NET backend (port 5000)
        ↓
Plan Generator — converts natural language to a structured JSON execution plan
  • Gemini / OpenAI: real NLP understands any phrasing ("overdue trials", "missed deadline")
  • Mock: keyword matching for local dev without an API key
        ↓
Validator — checks the plan against a strict allowlist BEFORE anything runs
  • Only approved services (study-service, corelabs-service) are permitted
  • Only approved fields, operators, and row limits are allowed
  • Write operations are unconditionally blocked
        ↓
Execution Engine — runs the approved plan against demo seed data
  • Verifies user roles and legal entities
  • Fetches studies + TestPs, correlates, classifies On Time / Delayed / Indeterminate
        ↓
Audit Service — records every query, who ran it, what plan was used, what it returned
        ↓
JSON response → Angular UI renders Summary + Results + Plan
```

The LLM never touches data. It only proposes a plan. The Validator decides whether that plan is safe to run — regardless of what the LLM said.

## Plan Generator Modes

| Mode | Activated when | Behaviour |
|---|---|---|
| **Gemini** (`gemini-2.5-flash`) | `Gemini:ApiKey` is configured (recommended — free tier) | Real NL intent extraction — understands paraphrases like "overdue trials", "missed deadline" |
| **OpenAI** (`gpt-4o-mini`) | `OpenAI:ApiKey` is configured (no Gemini key) | Real NL intent extraction with strict JSON schema |
| **Mock** (keyword match) | No API key (default) | Matches hardcoded phrases — suitable for local dev and tests without an API key |

## Quick Start

### 1. Backend API
```bash
cd elims-insight-assistant/backend/src/ElimsInsightAssistant.Api
dotnet run --urls http://localhost:5000
```
Swagger UI: http://localhost:5000/swagger

### 2. Add an API Key (optional — skip for mock mode)

Without a key the app runs in **mock mode** (keyword matching, no real NLP). You will see:
```
warn: Plan generator: MockPlanGenerator (keyword matching only — no real NLP).
```

To enable real NL intent extraction, choose a provider:

#### Option A — Google Gemini (recommended — free tier, no billing)
1. Go to **https://aistudio.google.com** and sign in
2. Click **Get API key** → **Create API key in new project** (starts with `AIza...`)
3. Set the key — never commit it to git:

```bash
# Recommended: stored in OS user profile, not in the project
dotnet user-secrets set "Gemini:ApiKey" "AIza..."
```

> **Important — User Secrets only work in Development environment.**
> Run with the environment set explicitly:
> ```powershell
> # Windows PowerShell
> $env:ASPNETCORE_ENVIRONMENT = "Development"
> dotnet run --urls http://localhost:5000
> ```
> ```bash
> # Mac / Linux
> ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5000
> ```
> Without this, .NET runs in Production mode and ignores User Secrets silently.
> You will see `warn: MockPlanGenerator` even if the key is set.
>
> **Alternatively**, set the key as an environment variable (works in any environment):
> ```powershell
> $env:Gemini__ApiKey = "AIza..."   # Windows PowerShell (double underscore)
> dotnet run --urls http://localhost:5000
> ```

**Free tier limits (Google AI Studio):** 5 requests/minute, 20 requests/day — enough for demos and hackathons. Use `gemini-2.5-flash` specifically; older models (gemini-2.0-flash, gemini-1.5-flash) may have zero quota on new projects.

When the Gemini key is active, startup logs:
```
info: Plan generator: GeminiPlanGenerator (gemini-2.5-flash, JSON mode)
```

#### Option B — OpenAI (requires billing)
```bash
dotnet user-secrets set "OpenAI:ApiKey" "sk-proj-..."
```
Startup logs: `info: Plan generator: OpenAiPlanGenerator (gpt-4o-mini, structured outputs)`

**Priority:** If both keys are present, Gemini is used. Remove `Gemini:ApiKey` to use OpenAI.

### 3. Run Tests
```bash
cd elims-insight-assistant/backend/src/ElimsInsightAssistant.Tests
dotnet test
```

### 4. Frontend
```bash
cd elims-insight-assistant/frontend/elims-insight-assistant-ui
npm install        # first time only — installs Angular 21 + dependencies
npx ng serve       # starts dev server at http://localhost:4200
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

## UI Overview

Open `http://localhost:4200` after starting both backend and frontend.

**Initial state** — input pre-filled, four example buttons:
```
eLIMS Insight Assistant
Governed Natural-Language Analytics

Ask eLIMS  [Find studies not completed on time      ] [Run Query]

[Find studies not completed on time] [Show delayed studies]
[Show indeterminate studies] [Show completed late studies]
```

**After clicking Run Query** — four sections appear below:
```
── Summary ──────────────────────────────────────
On Time: 2 | Delayed: 1 | Indeterminate: 1

── Results ──────────────────────────────────────
ST-002 (XYZ Labs)  → Delayed        2 days after planned date
ST-004 (Delta Bio) → Indeterminate  Missing planned/actual dates

── Generated Plan ───────────────────────────────
# Analysis Plan
Intent: Find studies not completed on time.
Steps: Fetch studies → Fetch TestPs → Correlate → Classify → Return

── JSON Execution Plan ──────────────────────────
{ "version": "1.0", "intent": "find_studies_not_completed_on_time", ... }
```

See `docs/build-from-scratch.md §Part 15` for annotated screen-by-screen walkthrough.

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
| 503 `status: ServiceUnavailable` | LLM provider unavailable (network/outage) | Retry with backoff |

Raw exception details are **never** in HTTP responses — they are logged server-side only.

## Project Structure

```
elims-insight-assistant/
├── backend/src/
│   ├── ElimsInsightAssistant.Api/
│   │   ├── Controllers/     HTTP entry points
│   │   ├── Services/        Plan generation (Gemini, OpenAI, Mock), classification, data clients
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

The LLM (Gemini, OpenAI, or none in mock mode) only proposes a JSON plan. Before any data is fetched:
- The plan is checked against a strict allowlist (services, fields, operators)
- The user's roles and legal entities are verified
- Write operations are unconditionally blocked

This makes the system safe for regulated environments regardless of what the LLM returns.
