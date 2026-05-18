# Service Contracts

This file defines every downstream service the execution engine is permitted to call,
along with the fields, actions, and filter operators allowed for each.

---

## Currently Registered Contracts

### `study-service`

| Property | Value |
|---|---|
| **Action** | `listStudies` |
| **Description** | Returns the full study catalogue visible to the requesting user |
| **Allowed fields** | `studyId`, `studyCode`, `customer`, `legalEntity`, `plannedCompletionDate` |
| **Demo client** | `DemoStudyServiceClient` — reads `seed-data/studies.json` |
| **Auth filter** | Results are filtered to `userContext.LegalEntities` before classification |

**Seed schema:**
```json
[
  {
    "studyId": "S1",
    "studyCode": "ST-001",
    "customer": "ABC Pharma",
    "legalEntity": "EU",
    "plannedCompletionDate": "2026-04-10"
  }
]
```

---

### `corelabs-service`

| Property | Value |
|---|---|
| **Action** | `listTestPs` |
| **Description** | Returns TestP execution records including status and completion timestamps |
| **Allowed fields** | `testpId`, `studyId`, `status`, `completedAt`, `runType`, `result` |
| **Demo client** | `DemoCoreLabsServiceClient` — reads `seed-data/testps.json` |
| **Default filter** | Execution engine keeps only records with `status = "Completed"` and `completedAt != null` |

**Seed schema:**
```json
[
  {
    "testpId": "TP1",
    "studyId": "S1",
    "status": "Completed",
    "completedAt": "2026-04-09T12:00:00",
    "runType": "Primary",
    "result": "Pass"
  }
]
```

---

## Allowed Filter Operators (all services)

| Operator | Example |
|---|---|
| `=` | `status = "Completed"` |
| `!=` | `status != "Cancelled"` |
| `>` | `completedAt > "2026-01-01"` |
| `>=` | `completedAt >= "2026-01-01"` |
| `<` | `plannedCompletionDate < "2026-06-01"` |
| `<=` | `actualCompletionDate <= plannedCompletionDate` |
| `in` | `legalEntity in ["EU","US"]` |
| `between` | `completedAt between "2026-01-01" and "2026-06-30"` |
| `is null` | `plannedCompletionDate is null` |
| `is not null` | `completedAt is not null` |

## Allowed Aggregate Functions (all services)

`max`, `min`, `count`, `sum`, `avg`

---

## Adding a New Service Contract

See `docs/solution-overview.md § Extending the Design` for the full step-by-step guide.

Quick reference — files to touch:

```
Services/NewDataServices.cs      ← interface + demo client + DTO
seed-data/<service>.json         ← seed data
Program.cs                       ← DI registration
Validation/PlanValidator.cs      ← add to Allowlist dictionary
Services/PlanGenerator.cs        ← add to ALLOWED SERVICES in all prompts
Execution/ExecutionEngine.cs     ← inject + call in ExecuteAsync
```

The validator's `RequiredServices` set (`{ "study-service", "corelabs-service" }`) defines
which services **must** appear in every valid plan. If a new service is optional (only used
for certain intents), do **not** add it to `RequiredServices` — the allowlist entry is
sufficient to permit it when the LLM asks for it.
