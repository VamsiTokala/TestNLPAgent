# Natural-Language Analytical Query Assistant for eLIMS

## Objective
Enable users to ask natural-language analytical questions (for example, **“Find studies not completed on time.”**) while ensuring:
- only approved service contracts are used,
- all backend execution is deterministic and validated,
- generated plans are auditable and reproducible.

## Scope and Contract Boundaries
The assistant uses only approved APIs:
- **Study Service contract** (study metadata, planned completion date, identifiers, customer)
- **CoreLabs Service contract** (TestP records, status, completion timestamps, run outcomes)

No direct database querying or unapproved endpoints are allowed.

## End-to-End Flow
1. **User NL query input**
2. **Internal LLM interpretation**
   - Identifies intent, entities, filters, grouping, and output shape.
   - Maps request to approved contract fields/operations.
3. **Plan generation (auditable MD + JSON)**
   - Markdown: human-readable rationale and steps.
   - JSON: machine-validated execution definition.
4. **Validation gate**
   - Schema validation of plan JSON.
   - Contract allowlist checks (operations, fields, joins, aggregations).
   - Deterministic constraints check (no free-form code/tool execution).
5. **Deterministic backend execution**
   - Calls approved services.
   - Applies filtering, grouping, date-window logic, and aggregations deterministically.
6. **Result classification + rendering**
   - Completed on time
   - Delayed
   - Indeterminate (missing/incomplete data)
7. **Audit logging**
   - Original NL query, generated MD plan, validated JSON execution plan, execution trace, output snapshot.

## Canonical Rule: “Study completed on time”
For each study:
1. Retrieve `plannedCompletionDate` from Study Service.
2. Retrieve all related TestPs from CoreLabs Service.
3. Derive `actualCompletionDate` as the **maximum** TestP completion timestamp across TestPs linked to the study.
4. Compare:
   - If `actualCompletionDate <= plannedCompletionDate` → **On time**
   - If `actualCompletionDate > plannedCompletionDate` → **Delayed**
   - If planned date missing, no completed TestPs, or insufficient timestamps → **Indeterminate**

## Deterministic Execution Definition (JSON shape)
```json
{
  "version": "1.0",
  "intent": "find_studies_not_completed_on_time",
  "entities": ["study", "testp"],
  "operations": [
    {
      "service": "study-service",
      "action": "listStudies",
      "select": ["studyId", "studyCode", "customer", "plannedCompletionDate"],
      "filters": []
    },
    {
      "service": "corelabs-service",
      "action": "listTestPs",
      "select": ["testpId", "studyId", "status", "completedAt", "runType", "result"],
      "filters": [{"field": "status", "op": "=", "value": "Completed"}]
    }
  ],
  "transform": {
    "groupBy": ["studyId"],
    "aggregates": [
      {"field": "completedAt", "fn": "max", "as": "actualCompletionDate"}
    ]
  },
  "compare": {
    "left": "actualCompletionDate",
    "op": ">",
    "right": "plannedCompletionDate",
    "as": "isDelayed"
  },
  "classify": {
    "onTime": "actualCompletionDate <= plannedCompletionDate",
    "delayed": "actualCompletionDate > plannedCompletionDate",
    "indeterminate": "plannedCompletionDate is null OR actualCompletionDate is null"
  },
  "output": {
    "columns": [
      "studyId",
      "studyCode",
      "customer",
      "plannedCompletionDate",
      "actualCompletionDate",
      "classification"
    ]
  }
}
```

## Markdown Plan Example (auditable)
```md
# Analysis Plan
Intent: Find studies not completed on time.

## Contract mapping
- Study Service: study identity + planned completion date.
- CoreLabs Service: completed TestPs and completion timestamps.

## Steps
1. Fetch studies with planned completion date fields.
2. Fetch completed TestPs for those studies.
3. Group TestPs by study.
4. Derive actual study completion = max(TestP.completedAt).
5. Compare actual completion against planned completion.
6. Classify as On Time, Delayed, or Indeterminate.
7. Return delayed + supporting details.
```

## Query Patterns Supported
1. **Find all studies not completed on time**
   - Uses canonical delayed classification.
2. **Show TestPs completed last week for Study ABC**
   - Adds study filter and date-window filter (`completedAt` within last week).
3. **List studies where planned completion date is missing**
   - Null check on `plannedCompletionDate`.
4. **Find all production runs where more than 20 TestPs failed**
   - Filter `runType=Production`, group by run/study, count failed TestPs, threshold > 20.
5. **Show studies for customer X with pending TestPs**
   - Filter by customer, pending status check on associated TestPs.
6. **Find TestPs completed after the planned study completion date**
   - Join Study planned date with TestP completion date and compare record-level timestamps.

## Validation and Guardrails
- Strict JSON schema for execution plans.
- Allowlisted service actions/fields/operators.
- Deterministic aggregation functions only (`max`, `min`, `count`, `sum`, `avg` as approved).
- Deny execution if any non-approved contract field/operation is requested.
- Enforce bounded result sizes and pagination.

## Output Contract
Every response includes:
- Result rows
- Classification reason per row
- Data quality flags (missing planned date, missing completion timestamps)
- Plan ID and execution trace ID for auditability

## Non-Functional Requirements
- Reproducibility: same query + same data snapshot => same result.
- Observability: structured logs for interpretation, validation, execution time, and downstream calls.
- Security: service-to-service auth, PII-safe logging, role-based access checks on contract endpoints.
- Performance: asynchronous fan-out calls where allowed, deterministic merge pipeline.

## Suggested Implementation Components
- **NL Interpreter**: LLM prompt templates + constrained output parser.
- **Plan Validator**: JSON schema + contract policy engine.
- **Execution Engine**: deterministic planner/executor for service calls + in-memory transformations.
- **Audit Store**: immutable plan + execution metadata.
- **Result Presenter**: table + summary buckets (On time / Delayed / Indeterminate).
