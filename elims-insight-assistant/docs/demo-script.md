# Demo Script

## Setup

1. Start backend: `ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5000`
2. Start frontend: `npx ng serve` (http://localhost:4200)
3. Confirm startup log shows `GeminiPlanGenerator` (or `MockPlanGenerator` if no key)
4. Open Swagger: http://localhost:5000/swagger (keep in a tab for Step 6)

---

## Walk-through

### 1. Service Catalogue — understand what's registered

Before running any query, point to the **Service Catalogue** at the top of the page.

- Two contract cards: **study-service** [required] and **corelabs-service** [required]
- Each card shows: service name (monospace), action, description, and field chips
- Both are marked **required** — every plan must use both

Key point: *"These cards are the AI's menu. It only knows about services listed here."*

---

### 2. Default query — "not completed on time"

- Input pre-filled with **"Find studies not completed on time"**
- Click **Run Query**
- Walk through the **AI Interpretation panel**:
  - **Provider badge** — "Gemini 2.5 Flash" (or Mock)
  - **Intent Detected** — `find_studies_not_completed_on_time`
  - **Classification Filter** — Delayed ✓ included, Indeterminate ✓ included, On Time ✗ excluded
  - **Service Contract Selection** — both service cards shown with pipeline connector "⊕ JOIN studyId" and the AI's reason for each
  - **Validation** — all checks green
- Service Catalogue cards above now show "✓ Selected" on both
- Summary cards: **On Time: 2 | Delayed: 1 | Indeterminate: 1**
- Results table shows ST-002 (Delayed) and ST-004 (Indeterminate)

Key point: *"The AI explains its reasoning for every service it selected. The classification filter is set by the LLM, not a dropdown."*

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
- AI Interpretation panel shows the unsupported state (⊘ icon, reason message)
- No plan, no results, no empty JSON rendered

Key point: *"The UI never renders an empty plan — unsupported queries show a clear message."*

---

### 6. Register a new contract live (dynamic registry demo)

- Click **+ Register Contract** in the Service Catalogue panel
- Fill in:
  - Service Name: `sample-service`
  - Display Name: `Sample Service`
  - Action: `listSamples`
  - Fields: `sampleId, studyId, status, collectedAt`
  - Purpose for AI: `Provides sample collection records and collection timestamps`
- Click **Register Contract**
- A third card appears in the Service Catalogue instantly
- Run any query — the AI Interpretation panel's service list will now include `sample-service` in its consideration

Key point: *"No restart, no code change. The AI prompt, validator allowlist, and UI catalogue all updated in real time."*

---

### 7. Validation guardrail (Swagger)

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

### 8. Audit trail

After any successful query, copy the `traceId` from the response JSON.

```bash
curl http://localhost:5000/api/assistant/audit/{traceId}
```

Shows: original query, who ran it, which plan was used, services called, execution timestamps, result snapshot.

---

## Key Talking Points

| Point | Where to show it |
|---|---|
| AI explains its reasoning per service | Steps 2–4 (AI Interpretation panel → Service Contract Selection) |
| LLM never touches data — only proposes a plan | Step 7 (validator rejects bad plan before execution) |
| Natural language → structured classification filter | Steps 3–4 (same data, different `includeClassifications`) |
| Dynamic service registry — no code change needed | Step 6 (Register Contract live) |
| UI surfaces errors cleanly | Step 5 (UnsupportedQuery) |
| Governed, auditable | Step 8 (audit trail) |
| Swagger API reference | http://localhost:5000/swagger |
