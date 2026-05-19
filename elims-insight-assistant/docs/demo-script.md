# Demo Script

## Setup

1. Start backend: `ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5000`
2. Start frontend: `npx ng serve` (http://localhost:4200)
3. Confirm startup log shows available generators, e.g.:
   `info: Available plan generators: mock, openrouter`
4. Open Swagger: http://localhost:5000/swagger (keep in a tab for Step 7)

---

## Walk-through

### 1. Service Catalogue — understand what's registered

Before running any query, point to the **Service Catalogue** at the top of the page.

- **Four** contract cards: **study-service** [required], **corelabs-service** [required],
  **sample-service** [optional], **protocol-service** [optional]
- Each card shows: service name (monospace), action, description, and field chips
- Required services must appear in every valid plan; optional ones are selected by the AI only when relevant

Key point: *"These cards are the AI's menu. It can only call services listed here — the validator blocks anything else."*

---

### 1b. Provider Selector (if multiple keys configured)

If you have more than one AI key set, a **Provider Selector** appears in the query bar header.
You can switch between Gemini / OpenAI / OpenRouter / Mock per-query without restarting.

Key point: *"The governed plan pattern is identical regardless of which LLM generates the plan."*

---

### 2. Default query — "not completed on time"

- Input pre-filled with **"Find studies not completed on time"**
- Click **Run Query**
- Watch the **Pipeline Tracker** animate through 4 steps:

  | Step | While running | When complete |
  |---|---|---|
  | ① Sending query to AI provider | pulsing dots | provider name badge |
  | ② AI generating execution plan | pulsing dots | expands: intent · services + AI reasons · classification filter |
  | ③ Validating plan against allowlist | pulsing dots | expands: every check pill (Plan completeness, Service allowlist, Field allowlist, Read-only execution, …) |
  | ④ Executing against services | pulsing dots | expands: services called · On Time/Delayed/Indeterminate counts · first 3 result rows preview |

- After the tracker completes, the full **AI Interpretation panel** appears with the same detail in a richer layout
- Service Catalogue: **study-service** and **corelabs-service** show "✓ Selected";
  **sample-service** and **protocol-service** are dimmed (not needed for this query)
- Summary cards show counts across all 12 studies (On Time / Delayed / Indeterminate)
- Results table shows all Delayed and Indeterminate studies

Key point: *"The pipeline tracker shows exactly what the AI decided, what the validator checked, and what data came back — before the user even scrolls down."*

---

### 3. Classification filter — Indeterminate only

- Type **"filter studies with classification Indeterminate"** (or click the quick-pick button)
- AI Interpretation panel updates:
  - Classification Filter: Indeterminate ✓ included, Delayed ✗ excluded, On Time ✗ excluded
  - Same two services selected — same data, different filter
- Results: **only ST-004** (1 row)
- Expand **JSON Execution Plan** at the bottom — show `"includeClassifications": ["Indeterminate"]`

Key point: *"Same service contracts, same seed data — the LLM's classification filter is the only thing that changed."*

---

### 4. Classification filter — Delayed only

- Type **"Show delayed studies"**
- Classification Filter: Delayed ✓ included, others excluded
- Results: only ST-002

---

### 5. Unsupported query

- Type **"List all customers"**
- Pipeline tracker step ② shows **unsupported** badge and displays the AI's reason inline
- Steps ③ and ④ show **skipped**
- The full AI Interpretation panel below shows the ⊘ unsupported state with the same reason message
- No plan, no results, no empty JSON rendered

Key point: *"The UI never renders an empty plan — unsupported queries show a clear message at every level."*

---

### 6. Register a new contract live (dynamic registry demo)

- Click **+ Register Contract** in the Service Catalogue panel
- Fill in:
  - Service Name: `adverse-event-service`
  - Display Name: `Adverse Event Service`
  - Action: `listAdverseEvents`
  - Fields: `eventId, studyId, severity, reportedAt, resolved`
  - Purpose for AI: `Provides adverse event records including severity and reporting timestamps`
- Click **Register Contract**
- A fifth card appears in the Service Catalogue instantly
- Run a query mentioning adverse events — the AI will now consider this service

Key point: *"No restart, no code change. The AI prompt, validator allowlist, and UI catalogue all updated in real time."*

---

### 8. Validation guardrail (Swagger)

Open `http://localhost:5000/swagger` → `POST /api/assistant/plan/validate`

Submit a plan with blank intent:
```json
{
  "version": "1.0",
  "intent": "",
  "entities": [],
  "operations": [],
  "limits": { "maxRows": 500, "pagination": false }
}
```

Response → HTTP 400, `"Plan completeness": "Failed"`, errors list shows exactly what's missing.

Key point: *"The validator runs independently of the LLM. Any plan — from any source — must pass before data is read."*

---

### 9. Audit trail

After any successful query, copy the `traceId` from the response JSON.

```bash
curl http://localhost:5000/api/assistant/audit/{traceId}
```

Shows: original query, who ran it, which plan was used, services called, execution timestamps, result snapshot.

---

## Key Talking Points

| Point | Where to show it |
|---|---|
| Pipeline tracker shows AI evaluation in real time | Step 2 (watch steps animate, then expand with data) |
| AI explains its reasoning per service | Step 2 tracker ② + AI Interpretation panel |
| Every validation check visible | Step 2 tracker ③ (check pills) |
| Data received is shown before scrolling | Step 2 tracker ④ (counts + preview) |
| LLM never touches data — only proposes a plan | Step 8 (validator rejects bad plan before execution) |
| Natural language → structured classification filter | Steps 3–4 (same data, different `includeClassifications`) |
| Unsupported query handled gracefully | Step 5 (tracker shows skipped at ③ and ④) |
| Dynamic service registry — no code change needed | Step 6 (Register Contract live) |
| Governed, auditable | Step 9 (audit trail) |
| Swagger API reference | http://localhost:5000/swagger |
