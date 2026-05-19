# Validation Rules

`PlanValidator` runs after plan generation and before execution. Every check must pass;
a single failure returns HTTP 400 and prevents any data from being read.

---

## Check Groups

### 1. Plan Completeness

Ensures the LLM returned a structurally complete plan, not a partial or confused response.

| Rule | Passes when |
|---|---|
| Intent present | `plan.Intent` is non-null and non-whitespace |
| Operations non-empty | `plan.Operations` contains at least one entry |
| Required services present | all contracts with `IsRequired=true` in `IServiceRegistry` appear in operations (currently: `study-service`, `corelabs-service`; optional: `sample-service`, `protocol-service`) |
| Entities non-empty | `plan.Entities` contains `"study"` and `"testp"` |

These checks stop a blank-intent / empty-operations plan (which a confused LLM can
produce when `supported=true`) from ever reaching the execution engine.

**Note:** `GeminiPlanGenerator` also applies a post-parse semantic check before
the validator runs — if intent is blank or operations is empty, it returns
`UnsupportedQuery` immediately and logs a warning.

---

### 2. Service Allowlist

Every `operation.service` must match a contract registered in `IServiceRegistry`.
The allowlist is derived at validation time — `PlanValidator(IServiceRegistry registry)` calls
`registry.GetAll()` so newly registered contracts are immediately permitted without a restart.

Currently registered (built-in):
- `study-service` [REQUIRED], `corelabs-service` [REQUIRED]
- `sample-service` [OPTIONAL], `protocol-service` [OPTIONAL]

Additional contracts can be registered via `POST /api/assistant/contracts` or the UI **+ Register Contract** button.

Any service name not in the registry is rejected — the LLM cannot invent new service endpoints.

---

### 3. Field Allowlist

Every field in `operation.select` must be in that service's permitted field list:

| Service | Permitted fields |
|---|---|
| `study-service` | `studyId`, `studyCode`, `customer`, `legalEntity`, `plannedCompletionDate` |
| `corelabs-service` | `testpId`, `studyId`, `status`, `completedAt`, `runType`, `result` |
| `sample-service` | `sampleId`, `studyId`, `sampleType`, `status`, `collectedAt`, `collectionSite` |
| `protocol-service` | `protocolId`, `studyId`, `version`, `status`, `approvedAt`, `expiresAt` |

Field lists are also derived from the registry — each contract's `Fields` array defines what is permitted for that service.

---

### 4. Filter Operator Allowlist

Every `filter.op` (case-insensitive) must be one of:
`=`, `!=`, `>`, `>=`, `<`, `<=`, `in`, `between`, `is null`, `is not null`

---

### 5. Injection Guard

Every `filter.value` is scanned for forbidden tokens:

```
select   from   drop   script   connectionstring   update   delete   insert
```

A match rejects the plan immediately regardless of context.

---

### 6. Read-Only Guard

Any `operation.action` that contains `update`, `delete`, or `write`
(case-insensitive) is unconditionally rejected — even if it somehow passed the
service allowlist.

---

### 7. Aggregate Allowlist

Every `transform.aggregate.fn` (case-insensitive) must be one of:
`max`, `min`, `count`, `sum`, `avg`

---

### 8. Result Limit

`plan.Limits.MaxRows` must be:
- Present and > 0
- ≤ 500

---

## ValidationResult Shape

```json
{
  "status": "Passed | Failed",
  "checks": [
    { "name": "Plan completeness",   "status": "Passed | Failed" },
    { "name": "Service allowlist",   "status": "Passed | Failed" },
    { "name": "Field allowlist",     "status": "Passed | Failed" },
    { "name": "Read-only execution", "status": "Passed | Failed" },
    { "name": "Aggregation allowlist","status": "Passed | Failed" },
    { "name": "Result limit",        "status": "Passed | Failed" },
    { "name": "User authorization",  "status": "Passed" }
  ],
  "errors": ["Intent is required", "Required service missing: corelabs-service"]
}
```

`errors` contains one entry per failing rule. The controller returns the full
`ValidationResult` in the HTTP 400 body so callers know exactly what failed.

---

## What the Validator Does NOT Check

- **Business logic correctness** — the validator does not know if the plan will
  return meaningful results; that is the execution engine's job.
- **Output.IncludeClassifications** — any combination of `"On Time"`, `"Delayed"`,
  `"Indeterminate"` is permitted; filtering is enforced at execution time.
- **plan.Correlate / plan.Transform / plan.Classify** — these declarative fields are
  parsed and stored but the execution engine uses its own hardcoded logic, so the
  validator does not re-check them (the allowlist on operations and fields is sufficient).
