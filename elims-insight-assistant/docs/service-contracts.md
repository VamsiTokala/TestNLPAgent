# Service Contracts

Service contracts define every downstream service the assistant is permitted to call,
along with its allowed actions, fields, and the purpose description injected into the AI prompt.

Contracts are managed by `IServiceRegistry` / `InMemoryServiceRegistry`. They drive the AI prompt,
the validator allowlist, the required-service check, and the UI catalogue — all from one place.

---

## API Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/assistant/contracts` | Returns all registered service contracts |
| `POST` | `/api/assistant/contracts` | Registers a new contract; returns updated list |

**POST body:**
```json
{
  "name":        "sample-service",
  "displayName": "Sample Service",
  "action":      "listSamples",
  "fields":      ["sampleId", "studyId", "status", "collectedAt"],
  "purpose":     "Provides sample collection records and collection timestamps",
  "description": "Sample collection catalogue",
  "isRequired":  false
}
```

---

## Built-in Contracts (seeded at startup)

Four contracts are seeded in `InMemoryServiceRegistry` at startup.
Two are **required** (every valid plan must include them); two are **optional** (AI selects them only when relevant).

### `study-service` [REQUIRED]

| Property | Value |
|---|---|
| **Action** | `listStudies` |
| **Purpose (AI prompt)** | Provides the study catalogue including planned completion dates, customers, and legal entities |
| **Description (UI)** | Study catalogue — planned completion dates, customers, legal entities |
| **Allowed fields** | `studyId`, `studyCode`, `customer`, `legalEntity`, `plannedCompletionDate` |
| **Demo client** | `DemoStudyServiceClient` — reads `seed-data/studies.json` |
| **Auth filter** | Results are filtered to `userContext.LegalEntities` before classification |
| **IsRequired** | `true` — must appear in every valid plan |

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

### `corelabs-service` [REQUIRED]

| Property | Value |
|---|---|
| **Action** | `listTestPs` |
| **Purpose (AI prompt)** | Provides TestP execution records with completion timestamps used to derive actual study completion dates |
| **Description (UI)** | TestP execution records — completion timestamps, status, run type |
| **Allowed fields** | `testpId`, `studyId`, `status`, `completedAt`, `runType`, `result` |
| **Demo client** | `DemoCoreLabsServiceClient` — reads `seed-data/testps.json` |
| **Default filter** | Execution engine keeps only records with `status = "Completed"` and `completedAt != null` |
| **IsRequired** | `true` — must appear in every valid plan |

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

---

### `sample-service` [OPTIONAL]

| Property | Value |
|---|---|
| **Action** | `listSamples` |
| **Purpose (AI prompt)** | Provides bioanalytical sample records — sample type, collection site, status, and collection timestamps |
| **Description (UI)** | Bioanalytical sample catalogue — collection timestamps, sample type, status, and site linkage |
| **Allowed fields** | `sampleId`, `studyId`, `sampleType`, `status`, `collectedAt`, `collectionSite` |
| **IsRequired** | `false` — AI selects it only when the query involves samples |

> No backend execution client is wired — the AI considers this service for routing decisions; backend integration requires adding a `DemoSampleServiceClient` and wiring it in `ExecutionEngine`. See `docs/solution-overview.md § Option B`.

---

### `protocol-service` [OPTIONAL]

| Property | Value |
|---|---|
| **Action** | `listProtocols` |
| **Purpose (AI prompt)** | Provides study protocol records — protocol version, approval status, and expiry dates |
| **Description (UI)** | Study protocol catalogue — approved versions, status, and expiry timeline |
| **Allowed fields** | `protocolId`, `studyId`, `version`, `status`, `approvedAt`, `expiresAt` |
| **IsRequired** | `false` — AI selects it only when the query involves protocols or approvals |

> No backend execution client is wired — same note as `sample-service`.

---

## How the Registry Drives Each Subsystem

### 1. AI Prompt (`PromptBuilder.CoreInstructions`)

Every registered contract is listed in the prompt under `REGISTERED SERVICE CONTRACTS`:

```
REGISTERED SERVICE CONTRACTS (select only those needed to answer the query):
  corelabs-service [REQUIRED] → action: listTestPs
    Purpose: Provides TestP execution records with completion timestamps...
    Fields: testpId, studyId, status, completedAt, runType, result
  protocol-service [OPTIONAL] → action: listProtocols
    Purpose: Provides study protocol records — version, approval status, and expiry dates
    Fields: protocolId, studyId, version, status, approvedAt, expiresAt
  sample-service [OPTIONAL] → action: listSamples
    Purpose: Provides bioanalytical sample records — type, site, status, collection timestamps
    Fields: sampleId, studyId, sampleType, status, collectedAt, collectionSite
  study-service [REQUIRED] → action: listStudies
    Purpose: Provides the study catalogue including planned completion dates...
    Fields: studyId, studyCode, customer, legalEntity, plannedCompletionDate
```

The LLM selects required services always, and optional services only when the query warrants them.

The LLM selects services based on this block and provides a `reason` for each selection.

### 2. Validator Allowlist (`PlanValidator`)

`PlanValidator(IServiceRegistry registry)` calls `registry.GetAll()` at validation time:

- Allowed service names = `contracts.Select(c => c.Name)`
- Allowed fields per service = `contracts[i].Fields`
- Required services = `contracts.Where(c => c.IsRequired).Select(c => c.Name)`

No code changes needed when a new contract is registered.

### 3. UI Catalogue

`GET /api/assistant/contracts` feeds the Service Catalogue panel. Cards show:
- Service name, action, description, and field chips
- "✓ Selected" badge after a query if the AI included this service
- AI reason inline (the `reason` field from the plan operation)
- "required" or "optional" tag based on `IsRequired`

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

### Via the UI (zero code changes, runtime only)

1. Click **+ Register Contract** in the Service Catalogue panel.
2. Fill in all fields. The **Purpose** text is injected directly into the AI prompt.
3. Click **Register Contract**.

The contract is live immediately. Contracts are process-local (lost on restart with the default `InMemoryServiceRegistry`).

### Via code (persisted across restarts)

Seed the entry in `InMemoryServiceRegistry` and wire up an `ExecutionEngine` client.
See `docs/solution-overview.md § Extending the Design — Option B` for the full guide.

**Files to touch (Option B):**

| File | Change |
|---|---|
| `Services/ServiceRegistry.cs` | Seed entry in constructor |
| `Services/NewDataServices.cs` | Interface + demo client + DTO |
| `seed-data/<service>.json` | Seed data |
| `Program.cs` | DI registration |
| `Execution/ExecutionEngine.cs` | Inject + call in `ExecuteAsync` |

No changes needed to `PlanGenerator.cs`, `PlanValidator.cs`, or any frontend file.
