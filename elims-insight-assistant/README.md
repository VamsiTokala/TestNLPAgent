# eLIMS Insight Assistant: Governed Natural-Language Analytics

A governed NL analytics assistant for a laboratory information management system (LIMS).
Users type plain-English questions; the system generates a validated, audited execution plan
and returns structured results — without letting an AI freely query databases.

## How It Works

```
User types query in Angular UI
  e.g. "filter studies with classification Indeterminate"
        ↓
Angular POSTs to /api/assistant/query → .NET backend (port 5000)
        ↓
Plan Generator — converts natural language to a structured JSON execution plan
  • Gemini / OpenAI: real NLP — understands any phrasing and maps it to
    output.includeClassifications (e.g. ["Indeterminate"] for the query above)
  • Mock: keyword matching for local dev without an API key
  • Post-parse check: blank intent or empty operations → UnsupportedQuery (never executed)
        ↓
Validator — checks the plan against a strict allowlist BEFORE anything runs
  • Plan completeness: intent, operations, required services, entities all present
  • Only approved services (study-service, corelabs-service) are permitted
  • Only approved fields, operators, aggregates, and row limits are allowed
  • Write operations are unconditionally blocked
  • Filter value injection guard (SQL/script tokens rejected)
        ↓
Execution Engine — runs the approved plan against demo seed data
  • Verifies user roles (StudyViewer + CoreLabsViewer) and legal entities
  • Fetches studies + TestPs, correlates, classifies On Time / Delayed / Indeterminate
  • Filters results by plan.Output.IncludeClassifications
        ↓
Audit Service — records every query, who ran it, what plan was used, what it returned
        ↓
JSON response → Angular UI:
  • status = "Completed"      → renders Summary cards + Results table + Plan
  • status = "UnsupportedQuery" → shows reason message only (no empty plan rendered)
  • HTTP 4xx/5xx              → shows error banner
```

The LLM never touches data. It only proposes a plan. The Validator decides whether
that plan is safe to run — regardless of what the LLM said.

## Plan Generator Modes

| Mode | Activated when | Behaviour |
|---|---|---|
| **Gemini** (`gemini-2.5-flash`) | `Gemini:ApiKey` is configured (recommended — free tier) | Real NL intent extraction — understands paraphrases like "overdue trials", "missed deadline" |
| **OpenAI** (`gpt-4o-mini`) | `OpenAI:ApiKey` is configured, no Gemini key | Real NL intent extraction with strict JSON schema |
| **OpenRouter** (`openai/gpt-oss-120b:free`) | `OpenRouter:ApiKey` is configured, no Gemini/OpenAI key | OpenAI-compatible proxy — native structured output, free tier |
| **Mock** (keyword match) | No API key (default) | Matches hardcoded phrases — suitable for local dev and tests without an API key |

**Priority (default generator):** Gemini → OpenAI → OpenRouter → Mock.
When multiple keys are configured, the UI **Provider Selector** lets you pick per-query.

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

#### Option C — OpenRouter (free tier — `openai/gpt-oss-120b:free`)
1. Sign up at **https://openrouter.ai** and create an API key (starts with `sk-or-v1-...`)
2. Set the key:

```bash
dotnet user-secrets set "OpenRouter:ApiKey" "sk-or-v1-..."
```

Or set the model in `appsettings.json` under `OpenRouter:Model` (defaults to `openai/gpt-oss-120b:free`).

Startup logs: `info: Plan generator: OpenRouterPlanGenerator (openai/gpt-oss-120b:free)`

**Priority:** Gemini → OpenAI → OpenRouter → Mock. If multiple keys are set, all generators are registered and the UI Provider Selector lets you switch per-query.

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

**Service Catalogue** (top of page) — shows every registered service contract as a card.
Four contracts are seeded at startup: **study-service** [required], **corelabs-service** [required],
**sample-service** [optional], and **protocol-service** [optional].
After a query runs, cards light up with "✓ Selected" or dim to show which services the AI picked.

**Query bar** — pre-filled with an example query; four quick-pick buttons below.
A **Provider Selector** appears when more than one AI provider key is configured, letting you
switch between Gemini / OpenAI / OpenRouter / Mock per-query.

**Pipeline Tracker** (appears immediately on Run Query, stays visible after completion):
- **① Sending** — shows provider name when sent
- **② AI plan** — expands to show intent, selected services + AI reasons, classification filter
- **③ Validation** — expands to show every check pill by name (Plan completeness, Service allowlist, Field allowlist, Read-only execution, …)
- **④ Execution** — expands to show services called, On Time / Delayed / Indeterminate counts, first 3 result previews

**AI Interpretation panel** (full detail view, appears after tracker completes):
- **Provider badge** — which generator was used (Gemini 2.5 Flash / GPT-4o Mini / OpenRouter / Mock)
- **Intent Detected** — the `intent` string extracted from the query
- **Classification Filter** — all three classifications shown as "included" or "excluded" based on what the AI chose
- **Service Contract Selection** — pipeline cards for each selected service with the AI's per-operation reason
- **Validation** — pass/fail pill for every validator check

**Summary cards** — On Time / Delayed / Indeterminate counts across all studies.

**Results table** — rows matching the classification filter.

**Register a new contract** — click **+ Register Contract**, fill in the form, and the AI immediately considers it on the next query (no restart needed).

## API Endpoints

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/assistant/contracts` | List all registered service contracts |
| `POST` | `/api/assistant/contracts` | Register a new service contract |
| `POST` | `/api/assistant/query` | Full pipeline: generate → validate → execute → audit |
| `POST` | `/api/assistant/plan` | Generate plan only, no execution |
| `POST` | `/api/assistant/plan/validate` | Validate a plan without executing |
| `POST` | `/api/assistant/execute` | Execute a pre-built validated plan |
| `GET` | `/api/assistant/audit/{traceId}` | Retrieve audit record by trace ID |
| `GET` | `/api/demo/studies` | View seed study data |
| `GET` | `/api/demo/corelabs/testps` | View seed TestP data |

Swagger UI: `http://localhost:5000/swagger`
OpenAPI spec: `http://localhost:5000/swagger/v1/swagger.json`

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
│   │   ├── Services/        Plan generation (Gemini, OpenAI, Mock), service registry, classification, data clients
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

## Extending — Adding a New Service Contract

### Option A: Via the UI (zero code changes)

Click **+ Register Contract** in the Service Catalogue panel and fill in:
- **Service Name** — identifier used in plans (e.g. `sample-service`)
- **Action** — the one allowed read action (e.g. `listSamples`)
- **Fields** — comma-separated allowlisted fields
- **Purpose for AI** — injected into the prompt; tells the LLM why to use this service

The contract is live immediately. The AI prompt, validator allowlist, and UI catalogue all update automatically.

> Contracts registered at runtime are process-local (`InMemoryServiceRegistry`). Seed them in code for persistence across restarts.

### Option B: In code (persisted across restarts)

Seed the entry in `InMemoryServiceRegistry` and add an `ExecutionEngine` client.
See `docs/solution-overview.md § Extending the Design — Option B` for the full guide.

**Files to touch:**

| File | Change |
|---|---|
| `Services/ServiceRegistry.cs` | Seed entry in constructor |
| `Services/NewDataServices.cs` | Interface + demo client + DTO |
| `seed-data/<service>.json` | Seed data |
| `Program.cs` | DI registration |
| `Execution/ExecutionEngine.cs` | Inject + call in `ExecuteAsync` |

No changes to `PlanGenerator.cs`, `PlanValidator.cs`, or any frontend file.
