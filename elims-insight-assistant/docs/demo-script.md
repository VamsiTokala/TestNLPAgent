# Demo Script

## Setup

1. Start backend: `ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5000`
2. Start frontend: `npx ng serve` (http://localhost:4200)
3. Confirm startup log shows `GeminiPlanGenerator` (or `MockPlanGenerator` if no key)

---

## Walk-through

### 1. Default query — "not completed on time"

- Input is pre-filled with **"Find studies not completed on time"**
- Click **Run Query**
- Show the three summary cards: **On Time: 2 | Delayed: 1 | Indeterminate: 1**
- Results table shows:
  - ST-002 XYZ Labs — **Delayed** (Apr 17 actual vs Apr 15 planned — 2 days late)
  - ST-004 Delta Bio — **Indeterminate** (both dates null)
- Point out: *"Summary counts all four studies; results show only Delayed + Indeterminate"*

### 2. Classification filter — Indeterminate only

- Type **"filter studies with classification Indeterminate"** (or click **Show indeterminate studies**)
- Results show **only ST-004** — the filter is driven by `output.includeClassifications: ["Indeterminate"]`
- JSON Execution Plan (expand it) — show `"includeClassifications": ["Indeterminate"]`

### 3. Classification filter — Delayed only

- Type **"Show delayed studies"**
- Results show only ST-002
- Point out classification badge coloring

### 4. Unsupported query

- Type **"List all customers"**
- UI shows: *"Not supported: This query is not about study completion timeliness."*
- No plan, no results, no empty JSON rendered — guardrail working

### 5. Validation guardrail (Swagger or curl)

Open `http://localhost:5000/swagger` → `POST /api/assistant/plan/validate`

Submit a plan with blank intent:
```json
{ "version": "1.0", "intent": "", "entities": [], "operations": [], "limits": { "maxRows": 500, "pagination": false } }
```

Response → HTTP 400, `"Plan completeness": "Failed"`, errors list shows exactly what's missing.

### 6. Audit trail

After any successful query, copy the `traceId` from the response.

```bash
curl http://localhost:5000/api/assistant/audit/{traceId}
```

Shows: original query, plan used, who ran it, services called, execution timestamps, result snapshot.
Point out: *"Full audit trail — every query is recorded regardless of result"*

---

## Key Talking Points

| Point | Where to show it |
|---|---|
| LLM never touches data — only proposes a plan | Step 5 (validator rejects bad plan before execution) |
| Natural language → structured filter | Steps 2–3 (same data, different `includeClassifications`) |
| UI surfaces errors cleanly | Step 4 (UnsupportedQuery message) |
| Governed, auditable | Step 6 (audit trail) |
| Extensible to new services | `docs/solution-overview.md § Extending the Design` |
