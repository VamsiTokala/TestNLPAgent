# Build eLIMS Insight Assistant From Scratch
### A Complete Beginner's Guide — VS Code · .NET 8 · Angular

---

## Who This Guide Is For

This guide is written for someone who:
- Has never built a web application before
- Has never used Visual Studio Code, .NET, or Angular
- Wants to understand **why** decisions are made, not just **what** to type

By the end you will have built a working API that accepts plain-English questions like
*"show me studies not completed on time"* and returns structured, validated, audited results.

---

## What We Are Building

**eLIMS Insight Assistant** is a governed natural-language analytics tool for a
laboratory information management system. In plain English:

> A user types a question. The system turns it into a safe, validated plan.
> The plan is checked before anything runs. Results are returned and logged.

### Why Does It Work This Way?

In regulated industries (pharma, biotech) you cannot let an AI freely query databases.
Every query must be:
- **Predictable** — same input always produces same plan
- **Safe** — only approved data fields and services are accessible
- **Auditable** — every query, who ran it, and what it returned is recorded

This is why we do **not** let an AI directly execute queries. Instead:

```
User types question
      ↓
Plan Generator creates a structured JSON plan
      ↓
Validator checks the plan against an allowlist
      ↓
Execution Engine runs the approved plan
      ↓
Audit Service records everything
      ↓
Results returned to user
```

---

## Part 1 — Install Your Tools

### 1.1 Visual Studio Code (the editor)

**What it is:** A free text editor made by Microsoft, purpose-built for writing code.
It has syntax highlighting, autocomplete, a built-in terminal, and extensions for every language.

**Install:**
1. Go to https://code.visualstudio.com
2. Click **Download** — it detects your OS automatically
3. Run the installer with all defaults
4. When you see **"Add to PATH"**, make sure it is checked

**Verify:** Open a terminal, type `code --version`. You should see a version number.

> **What is a terminal?**
> Windows: Press `Win` key → type `cmd` → press Enter
> Mac: Press `Cmd+Space` → type `terminal` → press Enter
> It is a text-based window where you control your computer by typing commands.

---

### 1.2 .NET 8 SDK (the backend language runtime)

**What it is:** .NET is Microsoft's platform for building applications. The SDK (Software
Development Kit) includes everything needed to write, build, and run C# code.
C# is the language our backend API is written in.

**Why C# and .NET?**
- Strongly typed — the compiler catches mistakes before the program runs
- Excellent for building web APIs (ASP.NET Core)
- Used widely in enterprise and regulated industries

**Install:**
1. Go to https://dotnet.microsoft.com/download/dotnet/8.0
2. Click **Download SDK** under .NET 8.0
3. Run the installer with all defaults

**Verify:**
```
dotnet --version
```
Expected output: `8.0.xxx`

---

### 1.3 Node.js and npm (needed for Angular)

**What it is:** Node.js lets you run JavaScript outside a browser. npm (Node Package
Manager) is its package installer — similar to an app store for code libraries.

**Why do we need it for Angular?**
Angular is a frontend framework written in TypeScript. TypeScript needs to be
compiled into JavaScript. Node.js runs the Angular build tools that do this compilation.

**Install:**
1. Go to https://nodejs.org
2. Click the **LTS** button (Long Term Support — the stable version)
3. Run installer with all defaults

**Verify:**
```
node --version
npm --version
```

---

### 1.4 Angular CLI

**What it is:** A command-line tool that creates, builds, and runs Angular applications.
CLI stands for Command Line Interface.

**Install** (in your terminal):
```
npm install -g @angular/cli
```

The `-g` flag means "install globally" — available from any folder on your computer.

**Verify:**
```
ng version
```

---

### 1.5 VS Code Extensions

Open VS Code. Click the **Extensions icon** in the left sidebar (four squares icon).
Search and install each:

| Extension | Purpose |
|---|---|
| **C# Dev Kit** (`ms-dotnettools.csdevkit`) | C# syntax, autocomplete, run/debug |
| **.NET Core Test Explorer** (`formulahendry.dotnet-test-explorer`) | Run tests with a click |
| **Angular Language Service** (`angular.ng-template`) | Angular HTML autocomplete |
| **REST Client** (`humao.rest-client`) | Test your API without leaving VS Code |

---

## Part 2 — Understand the Project Structure

Before writing a single line of code, understand what you are building and why it is
organised this way.

```
elims-insight-assistant/
├── backend/
│   └── src/
│       ├── ElimsInsightAssistant.Api/       ← The web API (what users call)
│       │   ├── Controllers/                 ← HTTP entry points
│       │   ├── Services/                    ← Business logic
│       │   ├── Models/                      ← Data shapes
│       │   ├── Validation/                  ← Safety checking
│       │   ├── Execution/                   ← Running the plan
│       │   ├── Audit/                       ← Recording history
│       │   └── Program.cs                   ← Application startup
│       └── ElimsInsightAssistant.Tests/     ← Automated tests
├── frontend/
│   └── elims-insight-assistant-ui/          ← Angular web UI
└── seed-data/
    ├── studies.json                         ← Demo study records
    └── testps.json                          ← Demo test plan records
```

**Why separate backend and frontend?**

The backend (API) and frontend (UI) are kept separate because:
- They use different languages (C# vs TypeScript)
- They can be deployed independently
- Multiple frontends (web, mobile) can share the same backend
- Teams can work on them simultaneously

**Why separate folders inside the API?**

Each folder has a single responsibility. This is called **Separation of Concerns** —
one of the most important principles in software design:

- `Controllers` — only handle incoming HTTP requests and return responses
- `Services` — only contain business logic (the "thinking" code)
- `Models` — only define what data looks like
- `Validation` — only check whether a plan is safe
- `Execution` — only run approved plans
- `Audit` — only record what happened

If a bug appears in validation, you know exactly which file to look at.

---

## Part 3 — Create the Backend Project

### 3.1 Open VS Code and Create the Folder Structure

Open your terminal in VS Code with `` Ctrl+` ``.

```bash
mkdir elims-insight-assistant
cd elims-insight-assistant
mkdir -p backend/src
mkdir seed-data
```

> `mkdir` = make directory. `-p` = create parent folders too if they don't exist.

### 3.2 Create the .NET Solution

A **solution** (.sln file) is a container that groups related .NET projects together.
Think of it like a folder that knows about multiple projects and how they relate.

```bash
cd backend/src
dotnet new sln -n ElimsInsightAssistant
```

### 3.3 Create the API Project

```bash
dotnet new webapi -n ElimsInsightAssistant.Api --no-openapi
cd ElimsInsightAssistant.Api
```

**What `dotnet new webapi` does:**
Creates a new ASP.NET Core Web API project. ASP.NET Core is the framework for
building HTTP APIs in .NET. It handles routing (which URL calls which function),
serialisation (converting objects to JSON), and hosting (running a web server).

**Why `--no-openapi`?**
We will add Swagger ourselves to understand what it does.

### 3.4 Add NuGet packages

**What is NuGet?**
NuGet is the package manager for .NET — like npm for JavaScript. Running
`dotnet add package` downloads the library and adds it to your `.csproj` file.

**Swagger** — generates a web UI that documents and lets you test every API endpoint:

```bash
dotnet add package Swashbuckle.AspNetCore
```

**OpenAI SDK** — the official .NET client for calling OpenAI's chat API:

```bash
dotnet add package OpenAI --version 2.10.0
```

> The version is pinned to `2.10.0` because the structured outputs API
> (`CreateJsonSchemaFormat`) changed signature between minor versions.

### 3.5 Add UserSecretsId to the project file

User Secrets let you store API keys on your machine without ever putting them
in a file that gets committed to git. The `.csproj` needs a `<UserSecretsId>`
property to enable this feature.

Open `ElimsInsightAssistant.Api.csproj` in VS Code and add the highlighted line:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UserSecretsId>elims-insight-assistant-api</UserSecretsId>   ← ADD THIS
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="OpenAI" Version="2.10.0" />
    <PackageReference Include="Swashbuckle.AspNetCore" Version="6.9.0" />
  </ItemGroup>
</Project>
```

The value can be any unique string — the project name works fine. Without this,
`dotnet user-secrets set` fails with:
```
Could not find the global property 'UserSecretsId' in MSBuild project
```

**Your final `.csproj` should look exactly like the block above** — two package
references and the `UserSecretsId` property. Verify it matches before continuing.

### 3.7 Create the Test Project

```bash
cd ..
dotnet new xunit -n ElimsInsightAssistant.Tests
cd ElimsInsightAssistant.Tests
dotnet add reference ../ElimsInsightAssistant.Api/ElimsInsightAssistant.Api.csproj
```

**What is xUnit?**
xUnit is a testing framework for .NET. A test framework gives you tools to write
functions that verify your code works correctly. When you run `dotnet test`, every
function marked `[Fact]` is executed and pass/fail is reported.

**Why write tests?**
In regulated industries, tests are not optional — they are proof that your code
does what it claims. They also catch regressions (things that used to work but
broke after a change).

**What is `dotnet add reference`?**
It tells the test project where the API project is, so tests can use its classes.

### 3.8 Add Both Projects to the Solution

```bash
cd ..
dotnet sln add ElimsInsightAssistant.Api/ElimsInsightAssistant.Api.csproj
dotnet sln add ElimsInsightAssistant.Tests/ElimsInsightAssistant.Tests.csproj
```

---

## Part 4 — Define Your Data Models

**What is a model?**
A model is a C# class (or record) that defines the shape of your data. Think of it
as a template: "a Study always has a StudyId, StudyCode, Customer, LegalEntity,
and PlannedCompletionDate".

**Why define models first?**
Models are the contract between all layers of your application. Once you define what
a `StudyDto` looks like, your services, controllers, and tests all agree on the shape.

Create `ElimsInsightAssistant.Api/Models/AssistantModels.cs`:

```csharp
namespace ElimsInsightAssistant.Api.Models;

// The incoming request from the user
public record NaturalLanguageQueryRequest(string Query, UserContext UserContext);

// Who is making the request — used for authorisation and filtering
public record UserContext(string UserId, List<string> Roles, List<string> LegalEntities);

// A study from the Study Service
public record StudyDto(
    string StudyId,
    string StudyCode,
    string Customer,
    string LegalEntity,
    DateTime? PlannedCompletionDate  // nullable — some studies have no planned date
);

// A test plan (TestP) from CoreLabs
public record TestPDto(
    string TestpId,
    string StudyId,
    string Status,
    DateTime? CompletedAt,           // nullable — pending TestPs have no completion time
    string RunType,
    string? Result
);

// One classified study result
public record StudyCompletionResult
{
    public string StudyId { get; init; } = string.Empty;
    public string StudyCode { get; init; } = string.Empty;
    public string Customer { get; init; } = string.Empty;
    public DateTime? PlannedCompletionDate { get; init; }
    public DateTime? ActualCompletionDate { get; init; }
    public string Classification { get; init; } = string.Empty;  // "On Time", "Delayed", "Indeterminate"
    public string Reason { get; init; } = string.Empty;
    public List<string> DataQualityFlags { get; init; } = [];
}

// Summary counts across all results
public record QuerySummary(int OnTime = 0, int Delayed = 0, int Indeterminate = 0);

// The full response returned to the caller
public record AssistantQueryResponse
{
    public string PlanId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string Status { get; init; } = "Completed";
    public string MarkdownPlan { get; init; } = string.Empty;
    public ExecutionPlan JsonPlan { get; init; } = new();
    public ValidationResult Validation { get; init; } = new();
    public QuerySummary Summary { get; init; } = new();
    public List<StudyCompletionResult> Results { get; init; } = [];
    public string Message { get; init; } = string.Empty;
}

// The structured execution plan
public record ExecutionPlan
{
    public string Version { get; init; } = "1.0";
    public string Intent { get; init; } = string.Empty;
    public DateTime AsOfTimestamp { get; init; } = DateTime.UtcNow;
    public List<string> Entities { get; init; } = [];
    public List<PlanOperation> Operations { get; init; } = [];
    public PlanCorrelate Correlate { get; init; } = new();
    public PlanTransform Transform { get; init; } = new();
    public PlanClassificationRules Classify { get; init; } = new();
    public PlanOutput Output { get; init; } = new();
    public PlanLimits Limits { get; init; } = new(500, true);
}

public record PlanOperation(string Service, string Action, List<string> Select, List<PlanFilter> Filters);
public record PlanFilter(string Field, string Op, string? Value);
public record PlanCorrelate(string LeftEntity = "study", string RightEntity = "testp",
    string LeftField = "studyId", string RightField = "studyId");
public record PlanTransform(List<string>? GroupBy = null, List<PlanAggregate>? Aggregates = null)
{
    public List<string> GroupBy { get; init; } = GroupBy ?? ["studyId"];
    public List<PlanAggregate> Aggregates { get; init; } = Aggregates ??
        [new("completedAt", "max", "actualCompletionDate")];
}
public record PlanAggregate(string Field, string Fn, string As);
public record PlanClassificationRules(
    string OnTime = "actualCompletionDate <= plannedCompletionDate",
    string Delayed = "actualCompletionDate > plannedCompletionDate",
    string Indeterminate = "plannedCompletionDate is null OR actualCompletionDate is null");
public record PlanOutput(List<string>? IncludeClassifications = null, List<string>? Columns = null)
{
    public List<string> IncludeClassifications { get; init; } =
        IncludeClassifications ?? ["Delayed", "Indeterminate"];
    public List<string> Columns { get; init; } = Columns ?? ["studyId", "studyCode", "customer",
        "plannedCompletionDate", "actualCompletionDate", "classification", "reason", "dataQualityFlags"];
}
public record PlanLimits(int MaxRows, bool Pagination);

// Validation result returned after checking a plan
public record ValidationResult(string Status = "Passed",
    List<ValidationCheck>? Checks = null, List<string>? Errors = null)
{
    public List<ValidationCheck> Checks { get; init; } = Checks ?? [];
    public List<string> Errors { get; init; } = Errors ?? [];
}
public record ValidationCheck(string Name, string Status);

// Audit record — everything about one query execution
public record AuditRecord
{
    public string TraceId { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string OriginalQuery { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string MarkdownPlan { get; init; } = string.Empty;
    public ExecutionPlan JsonPlan { get; init; } = new();
    public string ValidationStatus { get; init; } = "Passed";
    public List<ValidationCheck> ValidationChecks { get; init; } = [];
    public List<string> ServicesCalled { get; init; } = [];
    public DateTime ExecutionStartedAt { get; init; }
    public DateTime ExecutionCompletedAt { get; init; }
    public QuerySummary ResultSummary { get; init; } = new();
    public List<StudyCompletionResult> ResultSnapshot { get; init; } = [];
}
```

**Key C# concept — `record` vs `class`:**
A `record` is a special type designed for data. It is immutable by default (values
cannot change after creation — `init` instead of `set`), has built-in equality
comparison, and is more concise than a class. Perfect for models that represent data.

**Key C# concept — `?` (nullable):**
`DateTime?` means "a DateTime that can be null". Without `?`, the compiler forces
you to always have a value. The `?` explicitly acknowledges that absence of data
is a valid state — which is honest and prevents null-reference crashes.

---

## Part 5 — Create the Seed Data

Seed data is fake but realistic data used for development and demos. Real data
from a database is not available during development, so we create plausible records.

Create `seed-data/studies.json`:
```json
[
  {"studyId":"S1","studyCode":"ST-001","customer":"ABC Pharma","legalEntity":"EU","plannedCompletionDate":"2026-04-10"},
  {"studyId":"S2","studyCode":"ST-002","customer":"XYZ Labs","legalEntity":"EU","plannedCompletionDate":"2026-04-15"},
  {"studyId":"S3","studyCode":"ST-003","customer":"BioTest","legalEntity":"US","plannedCompletionDate":"2026-04-20"},
  {"studyId":"S4","studyCode":"ST-004","customer":"Delta Bio","legalEntity":"EU","plannedCompletionDate":null}
]
```

Create `seed-data/testps.json`:
```json
[
  {"testpId":"T1","studyId":"S1","status":"Completed","completedAt":"2026-04-08T10:30:00Z","runType":"Production","result":"Pass"},
  {"testpId":"T2","studyId":"S1","status":"Completed","completedAt":"2026-04-09T15:00:00Z","runType":"Production","result":"Pass"},
  {"testpId":"T3","studyId":"S2","status":"Completed","completedAt":"2026-04-16T11:00:00Z","runType":"Production","result":"Pass"},
  {"testpId":"T4","studyId":"S2","status":"Completed","completedAt":"2026-04-17T09:30:00Z","runType":"Production","result":"Pass"},
  {"testpId":"T5","studyId":"S3","status":"Completed","completedAt":"2026-04-18T14:00:00Z","runType":"Production","result":"Pass"},
  {"testpId":"T6","studyId":"S4","status":"Pending","completedAt":null,"runType":"Production","result":null}
]
```

**What this data represents:**
- ST-001 has two completed TestPs, latest on Apr 9 — before planned Apr 10 → **On Time**
- ST-002 has two completed TestPs, latest on Apr 17 — after planned Apr 15 → **Delayed**
- ST-003 completed Apr 18 — before planned Apr 20 → **On Time** (but filtered out as US entity)
- ST-004 has no planned date and no completed TestPs → **Indeterminate**

---

## Part 6 — Build the Services Layer

### 6.1 Demo Data Services (reading seed data)

**Concept — Interface + Implementation:**
An `interface` defines *what* a service does (its contract) without saying *how*.
The `class` provides the actual implementation. This matters because:
- Tests can swap in a fake implementation without hitting real files
- The real implementation can be replaced later (e.g. real database) without
  changing any other code

Create `Services/DemoDataServices.cs`:

```csharp
using System.Text.Json;
using ElimsInsightAssistant.Api.Models;

namespace ElimsInsightAssistant.Api.Services;

// The interface — what any study service must be able to do
public interface IStudyServiceClient
{
    Task<List<StudyDto>> ListStudiesAsync();
}

// The interface — what any CoreLabs service must be able to do
public interface ICoreLabsServiceClient
{
    Task<List<TestPDto>> ListTestPsAsync();
}

// The implementation — reads from a JSON file on disk
public class DemoStudyServiceClient(IWebHostEnvironment env) : IStudyServiceClient
{
    public async Task<List<StudyDto>> ListStudiesAsync()
    {
        // Build path: go 3 levels up from Api folder to reach elims-insight-assistant/
        var file = Path.Combine(env.ContentRootPath, "..", "..", "..", "seed-data", "studies.json");
        var json = await File.ReadAllTextAsync(Path.GetFullPath(file));
        return JsonSerializer.Deserialize<List<StudyDto>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }
}

public class DemoCoreLabsServiceClient(IWebHostEnvironment env) : ICoreLabsServiceClient
{
    public async Task<List<TestPDto>> ListTestPsAsync()
    {
        var file = Path.Combine(env.ContentRootPath, "..", "..", "..", "seed-data", "testps.json");
        var json = await File.ReadAllTextAsync(Path.GetFullPath(file));
        return JsonSerializer.Deserialize<List<TestPDto>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }
}
```

**Concept — `async` / `await` / `Task`:**
File reads and network calls take time. `async` methods don't block the server
while waiting — they pause and let other requests proceed. `Task<T>` is the
promise of a future value. `await` says "wait here until this finishes, but don't
block everything else".

**Concept — `IWebHostEnvironment`:**
Injected by ASP.NET Core automatically. Gives you `ContentRootPath` — the folder
where the API project lives on disk, so you can build paths relative to it.

### 6.2 Classification Service

This is the core business logic. Given one study and its actual completion date,
decide whether it is On Time, Delayed, or Indeterminate.

Add to `Services/DemoDataServices.cs`:

```csharp
public interface IClassificationService
{
    StudyCompletionResult Classify(StudyDto study, DateTime? actualCompletionDate);
}

public class StudyCompletionClassificationService : IClassificationService
{
    public StudyCompletionResult Classify(StudyDto study, DateTime? actualCompletionDate)
    {
        var flags = new List<string>();
        var classification = "On Time";
        var reason = "Actual completion date is on or before planned completion date.";

        // Check for missing data first — if anything is missing, it is Indeterminate
        if (study.PlannedCompletionDate is null)
        {
            classification = "Indeterminate";
            flags.Add("missing_planned_completion_date");
        }

        if (actualCompletionDate is null)
        {
            classification = "Indeterminate";
            flags.Add("no_completed_testps");
        }

        // Only compare dates if we have both
        if (classification != "Indeterminate" && actualCompletionDate > study.PlannedCompletionDate)
        {
            classification = "Delayed";
            var days = (actualCompletionDate!.Value.Date - study.PlannedCompletionDate!.Value.Date).Days;
            reason = $"Actual completion date is {days} days after planned completion date.";
        }
        else if (classification == "Indeterminate")
        {
            reason = "Planned completion date or completed TestP timestamp is missing.";
        }

        return new StudyCompletionResult
        {
            StudyId = study.StudyId,
            StudyCode = study.StudyCode,
            Customer = study.Customer,
            PlannedCompletionDate = study.PlannedCompletionDate,
            ActualCompletionDate = actualCompletionDate,
            Classification = classification,
            Reason = reason,
            DataQualityFlags = flags
        };
    }
}
```

### 6.3 Plan Generator

**Why a plan generator instead of direct execution?**
This is the key architectural decision. A natural language query is ambiguous and
unsafe to execute directly. By first converting it to a structured JSON plan, we
create a checkpoint where a validator can inspect and reject it before anything runs.

**Why is the interface `async`?**
The plan generator makes an outbound HTTP call to OpenAI. Any I/O operation must
be `async` so the server does not freeze while waiting. Even `MockPlanGenerator`
uses `Task.FromResult(...)` to satisfy the interface — it wraps its synchronous
result in a completed Task at zero cost.

**Why `PlanGeneratorResult` instead of a plain tuple?**
A tuple `(markdown, plan, error)` cannot express *why* the plan is null. There are
two completely different failure modes that need different HTTP responses:

| Failure mode | Cause | Correct HTTP response |
|---|---|---|
| `UnsupportedQuery` | Model understood the query but it's out of scope | **200** — not an error; do not retry |
| `ServiceUnavailable` | Network outage, provider error, parse failure | **503** — transient; client should retry |

A `bool IsServerError` on `PlanGeneratorResult` lets the controller tell these apart.
Without it, a provider outage silently looks like an unsupported query — monitoring
can't detect the problem and clients won't retry.

Create `Services/PlanGenerator.cs`:

```csharp
using System.Text.Json;
using ElimsInsightAssistant.Api.Models;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace ElimsInsightAssistant.Api.Services;

// ─── Result type ──────────────────────────────────────────────────────────────
// IsServerError = false → query understood but not supported → controller returns 200
// IsServerError = true  → transient failure (network, provider, parse) → controller returns 503

public record PlanGeneratorResult(
    string Markdown,
    ExecutionPlan? Plan,
    string? Error,
    bool IsServerError = false);  // default false — opt-in for transient errors

public interface IPlanGenerator
{
    Task<PlanGeneratorResult> GenerateAsync(string query);
}

// ─── Mock (no API key required — local dev and tests) ────────────────────────

public class MockPlanGenerator : IPlanGenerator
{
    private static readonly string[] SupportedTerms =
        ["not completed on time", "delayed studies", "completed late", "not on time", "indeterminate"];

    public Task<PlanGeneratorResult> GenerateAsync(string query)
    {
        var q = query.ToLowerInvariant();
        if (!SupportedTerms.Any(q.Contains))
            return Task.FromResult(new PlanGeneratorResult(
                string.Empty, null,
                "This demo currently supports queries related to study completion timeliness."));
                // IsServerError defaults to false — this is a valid "not supported" response

        var markdown = """
# Analysis Plan
Intent: Find studies not completed on time.
...
## Execution mode
Read-only, deterministic, approved service contracts only.
""";

        var plan = new ExecutionPlan
        {
            Intent = "find_studies_not_completed_on_time",
            Entities = ["study", "testp"],
            Operations =
            [
                new("study-service", "listStudies",
                    ["studyId", "studyCode", "customer", "legalEntity", "plannedCompletionDate"], []),
                new("corelabs-service", "listTestPs",
                    ["testpId", "studyId", "status", "completedAt", "runType", "result"],
                    [new("status", "=", "Completed")])
            ]
        };

        return Task.FromResult(new PlanGeneratorResult(markdown, plan, null));
    }
}

// ─── OpenAI (real NL intent extraction with strict JSON schema output) ────────

public class OpenAiPlanGenerator : IPlanGenerator
{
    private readonly ChatClient _client;
    private readonly ILogger<OpenAiPlanGenerator> _logger;

    // JSON schema constrains the model's output to exactly the shape we need.
    // Unlike JSON mode (which only guarantees valid JSON), strict schema means
    // the provider rejects responses that don't match — no post-hoc surprises.
    private static readonly BinaryData ResponseSchema = BinaryData.FromString("""
    {
      "type": "object",
      "properties": {
        "supported": { "type": "boolean" },
        "reason":   { "anyOf": [{ "type": "string" }, { "type": "null" }] },
        "markdown": { "anyOf": [{ "type": "string" }, { "type": "null" }] },
        "plan": {
          "anyOf": [
            {
              "type": "object",
              "properties": {
                "version":    { "type": "string" },
                "intent":     { "type": "string" },
                "entities":   { "type": "array", "items": { "type": "string" } },
                "operations": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "service": { "type": "string" },
                      "action":  { "type": "string" },
                      "select":  { "type": "array", "items": { "type": "string" } },
                      "filters": {
                        "type": "array",
                        "items": {
                          "type": "object",
                          "properties": {
                            "field": { "type": "string" },
                            "op":    { "type": "string" },
                            "value": { "anyOf": [{ "type": "string" }, { "type": "null" }] }
                          },
                          "required": ["field", "op", "value"],
                          "additionalProperties": false
                        }
                      }
                    },
                    "required": ["service", "action", "select", "filters"],
                    "additionalProperties": false
                  }
                },
                "limits": {
                  "type": "object",
                  "properties": {
                    "maxRows":    { "type": "integer" },
                    "pagination": { "type": "boolean" }
                  },
                  "required": ["maxRows", "pagination"],
                  "additionalProperties": false
                }
              },
              "required": ["version", "intent", "entities", "operations", "limits"],
              "additionalProperties": false
            },
            { "type": "null" }
          ]
        }
      },
      "required": ["supported", "reason", "markdown", "plan"],
      "additionalProperties": false
    }
    """);

    private const string SystemPrompt = """
You are a governed analytics plan generator for a LIMS.
If the query is about study completion timeliness, set supported=true and populate markdown and plan.
If not, set supported=false and explain in reason. Set markdown and plan to null.
""";

    public OpenAiPlanGenerator(IConfiguration config, ILogger<OpenAiPlanGenerator> logger)
    {
        _logger = logger;
        var apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
        _client = new ChatClient("gpt-4o-mini", apiKey);
    }

    public async Task<PlanGeneratorResult> GenerateAsync(string query)
    {
        try
        {
            var completion = await _client.CompleteChatAsync(
                [new SystemChatMessage(SystemPrompt), new UserChatMessage(query)],
                new ChatCompletionOptions
                {
                    ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                        "plan_response", ResponseSchema, jsonSchemaIsStrict: true)
                });

            var json = completion.Value.Content[0].Text;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.GetProperty("supported").GetBoolean())
                return new PlanGeneratorResult(string.Empty, null,
                    root.GetProperty("reason").GetString() ?? "Unsupported query.");

            var markdown = root.GetProperty("markdown").GetString() ?? string.Empty;
            var plan = JsonSerializer.Deserialize<ExecutionPlan>(
                root.GetProperty("plan").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (plan is null)
            {
                _logger.LogError("OpenAI returned supported=true but plan was null. Raw: {Json}", json);
                return new PlanGeneratorResult(string.Empty, null,
                    "Plan generation service returned an unexpected response.", IsServerError: true);
            }

            return new PlanGeneratorResult(markdown, plan, null);
        }
        catch (JsonException ex)
        {
            // Strict schema makes this very unlikely — log full details for investigation
            _logger.LogError(ex, "OpenAI response failed JSON parse for query: {Query}", query);
            return new PlanGeneratorResult(string.Empty, null,
                "Plan generation service returned an unreadable response.", IsServerError: true);
        }
        catch (Exception ex)
        {
            // Network error, provider outage, auth failure — retryable
            _logger.LogError(ex, "OpenAI call failed for query: {Query}", query);
            return new PlanGeneratorResult(string.Empty, null,
                "Plan generation service is temporarily unavailable.", IsServerError: true);
        }
    }
}
```

**Concept — `PlanGeneratorResult` record vs tuple:**
The old tuple `(markdown, plan, error)` couldn't say *why* the plan was null.
The new record adds `IsServerError` — a named, typed flag. Named fields are
harder to misuse than positional tuple elements and make intent obvious at the call site.

**Concept — `Task.FromResult(...)`:**
When a method must be `async` to satisfy an interface but has no real I/O,
`Task.FromResult(value)` wraps the value in an already-completed Task.
It costs almost nothing and keeps the interface consistent.

**Concept — Structured Outputs vs JSON Mode:**

| | JSON Mode (`CreateJsonObjectFormat`) | Structured Outputs (`CreateJsonSchemaFormat`) |
|---|---|---|
| Guarantees | Valid JSON | Valid JSON *and* matches your schema |
| Missing fields | Possible | Prevented by the provider |
| Extra fields | Possible | Prevented (`additionalProperties: false`) |
| Wrong types | Possible | Prevented |

Structured outputs shift the enforcement to the provider level — before the
response even leaves OpenAI. This means your parsing code handles the happy
path only; the catch blocks are for genuine network/auth failures, not bad shapes.

**Concept — Never log or return raw `ex.Message` to clients:**
Exception messages often contain internal details: provider error codes, upstream
server names, request IDs. Returning these to API consumers is an information
disclosure risk and creates an unstable error contract (the message text is
implementation detail, not an API guarantee). The pattern is:
- `_logger.LogError(ex, ...)` — full exception recorded server-side for debugging
- Return a fixed, generic string to the client — stable, safe, and intentional

---

## Part 7 — Build the Validation Layer

**Why validate the plan?**
The plan generator could theoretically produce anything. Validation is a second,
independent check that enforces the allowlist — regardless of how the plan was
created. This is defence in depth: two separate systems must both agree before
execution proceeds.

Create `Validation/PlanValidator.cs`:

```csharp
using ElimsInsightAssistant.Api.Models;

namespace ElimsInsightAssistant.Api.Validation;

public interface IPlanValidator
{
    ValidationResult Validate(ExecutionPlan plan);
}

public class PlanValidator : IPlanValidator
{
    // The allowlist — only these services, actions, and fields are permitted
    private static readonly Dictionary<string, (HashSet<string> actions, HashSet<string> fields)> Allowlist = new()
    {
        ["study-service"] = (
            ["listStudies"],
            ["studyId", "studyCode", "customer", "legalEntity", "plannedCompletionDate"]
        ),
        ["corelabs-service"] = (
            ["listTestPs"],
            ["testpId", "studyId", "status", "completedAt", "runType", "result"]
        )
    };

    private static readonly HashSet<string> AllowedOperators =
        ["=", "!=", ">", ">=", "<", "<=", "in", "between", "is null", "is not null"];

    private static readonly HashSet<string> AllowedAggs =
        ["max", "min", "count", "sum", "avg"];

    private static readonly string[] ForbiddenTokens =
        ["select ", " from ", "drop ", "script", "connectionstring",
         "update ", "delete ", "insert "];

    public ValidationResult Validate(ExecutionPlan plan)
    {
        var checks = new List<ValidationCheck>();
        var errors = new List<string>();

        foreach (var op in plan.Operations)
        {
            if (!Allowlist.ContainsKey(op.Service))
            {
                errors.Add($"Unapproved service: {op.Service}");
            }
            else
            {
                var entry = Allowlist[op.Service];

                if (!entry.actions.Contains(op.Action))
                    errors.Add($"Unapproved action: {op.Service}.{op.Action}");

                foreach (var field in op.Select.Where(f => !entry.fields.Contains(f)))
                    errors.Add($"Unapproved field: {op.Service}.{field}");

                foreach (var filter in op.Filters)
                {
                    if (!AllowedOperators.Contains(filter.Op.ToLowerInvariant()))
                        errors.Add($"Unapproved operator: {filter.Op}");

                    // Detect SQL/script injection attempts in filter values
                    if (filter.Value is { Length: > 0 } v &&
                        ForbiddenTokens.Any(t => v.ToLowerInvariant().Contains(t)))
                        errors.Add("Potential code/SQL fragment detected");
                }
            }

            // No write operations ever
            if (op.Action.Contains("update", StringComparison.OrdinalIgnoreCase) ||
                op.Action.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
                op.Action.Contains("write", StringComparison.OrdinalIgnoreCase))
                errors.Add("Write/update/delete operation is forbidden");
        }

        foreach (var agg in plan.Transform.Aggregates)
            if (!AllowedAggs.Contains(agg.Fn.ToLowerInvariant()))
                errors.Add($"Unapproved aggregate function: {agg.Fn}");

        if (plan.Limits is null || plan.Limits.MaxRows <= 0)
            errors.Add("Missing maxRows");
        else if (plan.Limits.MaxRows > 500)
            errors.Add("maxRows greater than 500");

        // Build a named check result for each category
        checks.Add(new("Service allowlist",   errors.Any(e => e.Contains("service"))           ? "Failed" : "Passed"));
        checks.Add(new("Field allowlist",     errors.Any(e => e.Contains("field"))             ? "Failed" : "Passed"));
        checks.Add(new("Read-only execution", errors.Any(e => e.Contains("Write/update"))      ? "Failed" : "Passed"));
        checks.Add(new("Aggregation allowlist",errors.Any(e => e.Contains("aggregate"))        ? "Failed" : "Passed"));
        checks.Add(new("Result limit",        errors.Any(e => e.Contains("maxRows"))           ? "Failed" : "Passed"));

        return new ValidationResult(errors.Count == 0 ? "Passed" : "Failed", checks, errors);
    }
}
```

**Concept — Allowlist vs Blocklist:**
A blocklist tries to enumerate everything bad (SQL injection, XSS...). An allowlist
does the opposite: only explicitly approved items pass. Allowlists are safer because
they fail closed — anything not on the list is rejected by default.

---

## Part 8 — Build the Execution Engine

The execution engine runs after validation passes. It is the only place data is
actually fetched and processed.

Create `Execution/ExecutionEngine.cs`:

```csharp
using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;

namespace ElimsInsightAssistant.Api.Execution;

public interface IExecutionEngine
{
    Task<(QuerySummary summary, List<StudyCompletionResult> rows, List<string> servicesCalled)>
        ExecuteAsync(ExecutionPlan plan, UserContext userContext);
}

public class ExecutionEngine(
    IStudyServiceClient studyClient,
    ICoreLabsServiceClient coreLabsClient,
    IClassificationService classificationService) : IExecutionEngine
{
    public async Task<(QuerySummary, List<StudyCompletionResult>, List<string>)>
        ExecuteAsync(ExecutionPlan plan, UserContext userContext)
    {
        // Step 1 — Authorisation check before touching any data
        if (!userContext.Roles.Contains("StudyViewer") || !userContext.Roles.Contains("CoreLabsViewer"))
            throw new UnauthorizedAccessException("User lacks required roles.");

        // Step 2 — Fetch studies, filtered to the user's permitted legal entities
        var studies = (await studyClient.ListStudiesAsync())
            .Where(s => userContext.LegalEntities.Contains(s.LegalEntity))
            .ToList();

        // Step 3 — Fetch only completed TestPs with a timestamp
        var completedTestPs = (await coreLabsClient.ListTestPsAsync())
            .Where(t => t.Status == "Completed" && t.CompletedAt is not null)
            .ToList();

        // Step 4 — For each study, find the LATEST TestP completion timestamp
        // (a study is only complete when ALL its TestPs are done — max = last one)
        var completionByStudy = completedTestPs
            .GroupBy(t => t.StudyId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.CompletedAt));

        // Step 5 — Classify every study
        var allResults = studies.Select(study =>
        {
            completionByStudy.TryGetValue(study.StudyId, out var actual);
            return classificationService.Classify(study, actual);
        }).ToList();

        // Step 6 — Build summary counts
        var summary = new QuerySummary(
            allResults.Count(r => r.Classification == "On Time"),
            allResults.Count(r => r.Classification == "Delayed"),
            allResults.Count(r => r.Classification == "Indeterminate"));

        // Step 7 — Apply output filter from the plan (e.g. only Delayed + Indeterminate)
        var filtered = allResults
            .Where(r => plan.Output.IncludeClassifications.Contains(r.Classification))
            .Take(plan.Limits.MaxRows)
            .ToList();

        return (summary, filtered, ["study-service", "corelabs-service"]);
    }
}
```

**Concept — LINQ (Language Integrated Query):**
Methods like `.Where()`, `.GroupBy()`, `.ToDictionary()`, `.Select()`, `.Max()` are
LINQ — a way to query collections in C# using a fluent, readable syntax.
`.Where(x => condition)` filters. `.Select(x => transform)` transforms.
`.GroupBy(x => key)` groups. It reads almost like English.

---

## Part 9 — Build the Audit Service

Every query must be recorded — who asked, what plan was generated, whether it
passed validation, what was returned. This is non-negotiable in regulated systems.

Create `Audit/AuditService.cs`:

```csharp
using System.Collections.Concurrent;
using ElimsInsightAssistant.Api.Models;

namespace ElimsInsightAssistant.Api.Audit;

public interface IAuditService
{
    void Save(AuditRecord record);
    AuditRecord? Get(string traceId);
}

public class InMemoryAuditService : IAuditService
{
    // ConcurrentDictionary is thread-safe — multiple requests can write simultaneously
    private readonly ConcurrentDictionary<string, AuditRecord> _records = new();

    public void Save(AuditRecord record) => _records[record.TraceId] = record;

    public AuditRecord? Get(string traceId) =>
        _records.TryGetValue(traceId, out var r) ? r : null;
}
```

**Concept — `ConcurrentDictionary`:**
A web server handles many requests at the same time. A regular `Dictionary` is not
safe for simultaneous writes — two requests writing at the same moment can corrupt
it. `ConcurrentDictionary` is designed for this: it handles concurrency internally.

**Why in-memory for a demo?**
In-memory means data is lost when the server restarts. For production you would
use a database. For a demo, in-memory is simpler and has no dependencies.

---

## Part 10 — Build the Controllers

Controllers are the entry points — they sit at the HTTP boundary, receive requests,
call services, and return responses. They should be thin: no business logic here.

Create `Controllers/AssistantController.cs`:

```csharp
using ElimsInsightAssistant.Api.Audit;
using ElimsInsightAssistant.Api.Execution;
using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;
using ElimsInsightAssistant.Api.Validation;
using Microsoft.AspNetCore.Mvc;

namespace ElimsInsightAssistant.Api.Controllers;

[ApiController]
[Route("api/assistant")]
public class AssistantController(
    IPlanGenerator planGenerator,
    IPlanValidator validator,
    IExecutionEngine executionEngine,
    IAuditService auditService) : ControllerBase
{
    // POST /api/assistant/query — the main endpoint
    [HttpPost("query")]
    public async Task<ActionResult<AssistantQueryResponse>> Query(
        [FromBody] NaturalLanguageQueryRequest request)
    {
        // 1. Generate plan from natural language
        var result = await planGenerator.GenerateAsync(request.Query);

        // Transient failure (network, provider outage) → 503 so clients know to retry
        if (result.IsServerError)
            return StatusCode(503, new { status = "ServiceUnavailable", message = result.Error });

        // Genuinely unsupported query → 200 with status flag (not an error, just out of scope)
        if (result.Plan is null)
            return Ok(new AssistantQueryResponse
                { Status = "UnsupportedQuery", Message = result.Error ?? "Unsupported query" });

        // 2. Validate the plan
        var validation = validator.Validate(plan);
        validation = validation with
            { Checks = [.. validation.Checks, new("User authorization", "Passed")] };
        if (validation.Status != "Passed")
            return BadRequest(validation);

        // 3. Execute and record timing
        var traceId = $"TRACE-{Guid.NewGuid():N}";
        var planId  = $"PLAN-{Guid.NewGuid():N}";
        var started = DateTime.UtcNow;
        var (summary, rows, servicesCalled) =
            await executionEngine.ExecuteAsync(plan, request.UserContext);
        var finished = DateTime.UtcNow;

        var response = new AssistantQueryResponse
        {
            PlanId = planId, TraceId = traceId,
            MarkdownPlan = markdown, JsonPlan = plan,
            Validation = validation, Summary = summary, Results = rows
        };

        // 4. Save audit record
        auditService.Save(new AuditRecord
        {
            TraceId = traceId, PlanId = planId,
            OriginalQuery = request.Query, UserId = request.UserContext.UserId,
            MarkdownPlan = markdown, JsonPlan = plan,
            ValidationStatus = validation.Status, ValidationChecks = validation.Checks,
            ServicesCalled = servicesCalled,
            ExecutionStartedAt = started, ExecutionCompletedAt = finished,
            ResultSummary = summary, ResultSnapshot = rows
        });

        return Ok(response);
    }

    // POST /api/assistant/plan — generate plan only, do not execute
    [HttpPost("plan")]
    public ActionResult<object> Plan([FromBody] NaturalLanguageQueryRequest request)
    {
        var (markdown, plan, error) = planGenerator.Generate(request.Query);
        if (plan is null) return Ok(new { status = "UnsupportedQuery", message = error });
        return Ok(new { markdownPlan = markdown, jsonPlan = plan });
    }

    // POST /api/assistant/plan/validate — validate a plan without executing it
    [HttpPost("plan/validate")]
    public ActionResult<ValidationResult> Validate([FromBody] ExecutionPlan plan) =>
        Ok(validator.Validate(plan));

    // POST /api/assistant/execute — execute a pre-built plan
    [HttpPost("execute")]
    public async Task<ActionResult<object>> Execute([FromBody] ExecuteRequest request)
    {
        var validation = validator.Validate(request.Plan);
        if (validation.Status != "Passed") return BadRequest(validation);
        var (summary, rows, _) = await executionEngine.ExecuteAsync(request.Plan, request.UserContext);
        return Ok(new { summary, results = rows, validation });
    }

    // GET /api/assistant/audit/{traceId} — look up a past query by trace ID
    [HttpGet("audit/{traceId}")]
    public ActionResult<AuditRecord> Audit(string traceId)
    {
        var record = auditService.Get(traceId);
        return record is null ? NotFound() : Ok(record);
    }
}

public record ExecuteRequest(ExecutionPlan Plan, UserContext UserContext);
```

Create `Controllers/StudyDemoController.cs`:
```csharp
using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ElimsInsightAssistant.Api.Controllers;

[ApiController]
[Route("api/demo/studies")]
public class StudyDemoController(IStudyServiceClient studyService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<StudyDto>>> Get() =>
        Ok(await studyService.ListStudiesAsync());
}
```

Create `Controllers/CoreLabsDemoController.cs`:
```csharp
using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ElimsInsightAssistant.Api.Controllers;

[ApiController]
[Route("api/demo/corelabs/testps")]
public class CoreLabsDemoController(ICoreLabsServiceClient coreLabsService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TestPDto>>> Get() =>
        Ok(await coreLabsService.ListTestPsAsync());
}
```

**Concept — Dependency Injection (DI):**
Notice the controller receives `IPlanGenerator`, `IPlanValidator`, etc. in its
constructor — it does not create them itself. This is Dependency Injection.
ASP.NET Core's DI container creates and wires up all these objects automatically.

Benefits:
- Easy to test — swap real services for fakes in tests
- Loose coupling — the controller doesn't care which implementation it gets
- Single place to configure everything — `Program.cs`

**Concept — `[ApiController]`, `[Route]`, `[HttpPost]`:**
These are **attributes** — metadata you attach to classes and methods.
`[ApiController]` enables automatic model validation and request binding.
`[Route("api/assistant")]` sets the URL prefix for all methods in this controller.
`[HttpPost("query")]` means this method handles POST requests to `/api/assistant/query`.

---

## Part 11 — Wire Everything Together in Program.cs

`Program.cs` is the startup file. It registers all services with the DI container
and configures the HTTP pipeline.

```csharp
using ElimsInsightAssistant.Api.Audit;
using ElimsInsightAssistant.Api.Execution;
using ElimsInsightAssistant.Api.Services;
using ElimsInsightAssistant.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

// Register services with the DI container
// AddSingleton — one instance shared by all requests (stateless services)
// AddScoped    — one instance per HTTP request (stateful per-request services)
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Use OpenAI when an API key is present; fall back to rule-based mock for local dev
var openAiKey = builder.Configuration["OpenAI:ApiKey"];
if (!string.IsNullOrWhiteSpace(openAiKey))
    builder.Services.AddSingleton<IPlanGenerator, OpenAiPlanGenerator>();
else
    builder.Services.AddSingleton<IPlanGenerator, MockPlanGenerator>();

builder.Services.AddSingleton<IPlanValidator,     PlanValidator>();
builder.Services.AddSingleton<IAuditService,      InMemoryAuditService>();
builder.Services.AddScoped<IStudyServiceClient,   DemoStudyServiceClient>();
builder.Services.AddScoped<ICoreLabsServiceClient,DemoCoreLabsServiceClient>();
builder.Services.AddScoped<IClassificationService,StudyCompletionClassificationService>();
builder.Services.AddScoped<IExecutionEngine,      ExecutionEngine>();

var app = builder.Build();

// Log which generator is active so developers always know the mode on startup.
// There is no silent fallback — the warning is intentional.
var logger = app.Services.GetRequiredService<ILogger<Program>>();
if (!string.IsNullOrWhiteSpace(openAiKey))
    logger.LogInformation("Plan generator: OpenAiPlanGenerator (gpt-4o-mini, structured outputs)");
else
    logger.LogWarning(
        "Plan generator: MockPlanGenerator (keyword matching only — no real NLP). " +
        "To enable real NL intent extraction set OpenAI:ApiKey. " +
        "See docs/build-from-scratch.md §14.1 for how to obtain and configure a key.");

// Configure the HTTP pipeline
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

Also create `appsettings.json` in the API project root (next to `Program.cs`).
This file provides the `OpenAI:ApiKey` config key — left empty so the app starts
in mock mode by default. Never put your real key here:

```json
{
  "OpenAI": {
    "ApiKey": ""
  }
}
```

Your real key goes in User Secrets (see §14.1) which override this file locally
and are never committed to git.

**Concept — `AddSingleton` vs `AddScoped`:**

| Lifetime | Created | Destroyed | Use when |
|---|---|---|---|
| `Singleton` | Once at startup | App shuts down | Stateless — same instance is safe for all requests |
| `Scoped` | Each HTTP request | Request ends | Has per-request state (like reading a file) |
| `Transient` | Every time it's requested | After each use | Lightweight, no shared state |

`InMemoryAuditService` uses `ConcurrentDictionary` so it's safe as a singleton.
`DemoStudyServiceClient` takes `IWebHostEnvironment` which is scoped, so it must be scoped too.

---

## Part 12 — Write the Tests

Tests prove your code works. Write one test per behaviour, not per function.

Create `ElimsInsightAssistant.Tests/GlobalUsings.cs`:
```csharp
global using Xunit;
```

Create `ElimsInsightAssistant.Tests/ClassificationTests.cs`:
```csharp
using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;

namespace ElimsInsightAssistant.Tests;

public class ClassificationTests
{
    private readonly StudyCompletionClassificationService _svc = new();

    [Fact]
    public void OnTime_WhenActualBeforePlanned()
    {
        var result = _svc.Classify(
            new StudyDto("S1","ST-001","C","EU", new DateTime(2026,4,10)),
            new DateTime(2026,4,9));
        Assert.Equal("On Time", result.Classification);
    }

    [Fact]
    public void OnTime_WhenEqual() =>
        Assert.Equal("On Time",
            _svc.Classify(new("S1","ST","C","EU", new DateTime(2026,4,10)),
                new DateTime(2026,4,10)).Classification);

    [Fact]
    public void Delayed_WhenAfter() =>
        Assert.Equal("Delayed",
            _svc.Classify(new("S1","ST","C","EU", new DateTime(2026,4,10)),
                new DateTime(2026,4,12)).Classification);

    [Fact]
    public void Indeterminate_WhenPlannedMissing() =>
        Assert.Equal("Indeterminate",
            _svc.Classify(new("S1","ST","C","EU", null),
                new DateTime(2026,4,12)).Classification);

    [Fact]
    public void Indeterminate_WhenActualMissing() =>
        Assert.Equal("Indeterminate",
            _svc.Classify(new("S1","ST","C","EU", new DateTime(2026,4,10)),
                null).Classification);

    [Fact]
    public void UsesMaxTimestampAcrossCompletedTestPs()
    {
        // Simulate: LINQ caller finds max date before passing to Classify
        var testps = new[] { new DateTime(2026,4,16), new DateTime(2026,4,17), new DateTime(2026,4,15) };
        var actual = testps.Max();
        var result = _svc.Classify(new("S2","ST-002","C","EU", new DateTime(2026,4,15)), actual);
        Assert.Equal(new DateTime(2026,4,17), result.ActualCompletionDate);
    }
}
```

Create `ElimsInsightAssistant.Tests/ValidatorTests.cs`:
```csharp
using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Validation;

namespace ElimsInsightAssistant.Tests;

public class ValidatorTests
{
    private readonly PlanValidator _validator = new();

    // A known-good plan used as the base for all tests
    private static ExecutionPlan ApprovedPlan => new()
    {
        Intent = "find_studies_not_completed_on_time",
        Operations =
        [
            new("study-service", "listStudies",
                ["studyId","studyCode","customer","legalEntity","plannedCompletionDate"], []),
            new("corelabs-service", "listTestPs",
                ["testpId","studyId","status","completedAt","runType","result"],
                [new("status","=","Completed")])
        ],
        Limits = new PlanLimits(500, true)
    };

    [Fact] public void AcceptsApprovedPlan() =>
        Assert.Equal("Passed", _validator.Validate(ApprovedPlan).Status);

    [Fact]
    public void RejectsUnapprovedService()
    {
        var plan = ApprovedPlan with
            { Operations = [new("evil-service","listStudies",["studyId"],[])] };
        Assert.Equal("Failed", _validator.Validate(plan).Status);
    }

    [Fact]
    public void RejectsUnapprovedField()
    {
        var plan = ApprovedPlan with
            { Operations = [new("study-service","listStudies",["studyId","secretField"],[])] };
        Assert.Equal("Failed", _validator.Validate(plan).Status);
    }

    [Fact]
    public void RejectsWriteOperation()
    {
        var plan = ApprovedPlan with
            { Operations = [new("study-service","deleteStudies",["studyId"],[])] };
        Assert.Equal("Failed", _validator.Validate(plan).Status);
    }

    [Fact]
    public void RejectsMissingMaxRows()
    {
        var plan = ApprovedPlan with { Limits = new PlanLimits(0, true) };
        Assert.Equal("Failed", _validator.Validate(plan).Status);
    }

    [Fact]
    public void RejectsMaxRowsOver500()
    {
        var plan = ApprovedPlan with { Limits = new PlanLimits(501, true) };
        Assert.Equal("Failed", _validator.Validate(plan).Status);
    }
}
```

**Concept — `with` expression:**
`ApprovedPlan with { Operations = [...] }` creates a copy of the record with only
the specified properties changed. Everything else stays the same. This is perfect
for tests: start with a known-good object and make exactly one change per test.

---

## Part 13 — Build the Angular Frontend

> **Angular 21 uses standalone components by default** — there is no NgModule.
> Each component declares its own dependencies in an `imports` array inside
> `@Component`. This section shows you how to create the project from scratch
> yourself, then walks through every file so you understand what it does and why.

### 13.1 Create the project with `ng new`

Open a terminal, navigate to the `frontend` folder, and run:

```bash
cd elims-insight-assistant/frontend
ng new elims-insight-assistant-ui --routing=false --style=scss
cd elims-insight-assistant-ui
```

**What each flag means:**
- `--routing=false` — we don't need multiple pages/routes; skip the router module
- `--style=scss` — use SCSS (CSS with variables and nesting) instead of plain CSS

**What `ng new` creates for you:**
```
elims-insight-assistant-ui/
├── angular.json        ← build + serve configuration
├── package.json        ← npm dependencies (Angular, RxJS, zone.js, etc.)
├── tsconfig.json       ← TypeScript compiler settings
├── tsconfig.app.json   ← extends tsconfig.json, used when building the app
├── src/
│   ├── index.html      ← the one HTML page; Angular injects everything into it
│   ├── main.ts         ← entry point; bootstraps the root component
│   ├── styles.scss     ← global styles
│   └── app/
│       ├── app.component.ts    ← root component Angular generates by default
│       ├── app.component.html  ← default "Hello world" template (we replace this)
│       └── app.component.scss
```

> **If you cloned this repository:** all these files already exist — you do not
> need to run `ng new`. Just run `npm install` inside
> `frontend/elims-insight-assistant-ui/` and skip to §13.2.
>
> **If you are building your own project from scratch:** run the `ng new` command
> above, then follow every step in this Part to replace and add the files.

After `ng new`, delete **only** the generated HTML template and spec file — do NOT
delete `app.component.ts` itself, you will replace its contents in §13.3:

```bash
rm src/app/app.component.html
rm src/app/app.component.spec.ts
```

> **Tip:** If you accidentally deleted `app.component.ts`, recreate it — the full
> replacement content is in §13.3 below.

Now create the feature folder structure:

```bash
mkdir -p src/app/features/insight-assistant/models
mkdir -p src/app/features/insight-assistant/services
```

Your project structure should now look like this:

```
frontend/elims-insight-assistant-ui/
├── angular.json
├── package.json
├── tsconfig.json
├── tsconfig.app.json
├── proxy.conf.json       ← you will create this in §13.8
├── src/
│   ├── index.html
│   ├── main.ts
│   ├── styles.scss
│   └── app/
│       ├── app.component.ts          ← rewrite this (§13.3)
│       └── features/insight-assistant/
│           ├── insight-assistant.component.ts   ← create (§13.6)
│           ├── insight-assistant.component.html ← create (§13.6)
│           ├── insight-assistant.component.scss ← create (§13.6)
│           ├── models/                          ← create files (§13.4)
│           └── services/
│               └── insight-assistant-api.service.ts  ← create (§13.5)
```

**Why no NgModule?**
In Angular 17+, standalone is the default. Instead of a central `AppModule` that
lists every component, each component lists its own dependencies in `imports: [...]`.
This removes a layer of indirection that caused the classic beginner error
"Component X is not declared in any NgModule."

### 13.2 Entry point — `src/main.ts`

`ng new` generates a `main.ts` that uses `bootstrapApplication`. Open it and make
sure it matches:

```typescript
import { bootstrapApplication } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { AppComponent } from './app/app.component';

bootstrapApplication(AppComponent, {
  providers: [provideHttpClient()]
}).catch(err => console.error(err));
```

**What this does:**
- `bootstrapApplication` starts Angular with a standalone root component — no NgModule
- `provideHttpClient()` registers Angular's HTTP service so any component or service
  can inject it and make API calls

### 13.3 Root component — `src/app/app.component.ts`

Open `src/app/app.component.ts` (created by `ng new`) and **replace its entire
contents** with the following. If you accidentally deleted the file, create it fresh
at that path with the same content:

```typescript
import { Component } from '@angular/core';
import { InsightAssistantComponent } from './features/insight-assistant/insight-assistant.component';

@Component({
  selector: 'app-root',
  imports: [InsightAssistantComponent],
  template: '<app-insight-assistant></app-insight-assistant>'
})
export class AppComponent {}
```

**Key points:**
- `imports: [InsightAssistantComponent]` — the standalone way to use another component.
  In the old NgModule style this was done in `declarations: [...]` inside a module file.
  Now each component brings in what it needs directly.
- The inline `template` replaces the deleted `app.component.html`. For a root shell
  component this is fine — there is only one line to render.

### 13.4 Create the Models

You need **four** model files. Create each one inside
`src/app/features/insight-assistant/models/` and make sure every file has the
content below saved before you build. An empty file with the right name is not
enough — the TypeScript compiler needs the exported interfaces.

`assistant-query-request.model.ts`:
```typescript
export interface UserContext {
  userId: string;
  roles: string[];
  legalEntities: string[];
}

export interface AssistantQueryRequest {
  query: string;
  userContext: UserContext;
}
```

`execution-plan.model.ts`:
```typescript
export interface PlanFilter {
  field: string;
  op: string;
  value: string | null;
}

export interface PlanOperation {
  service: string;
  action: string;
  select: string[];
  filters: PlanFilter[];
}

export interface PlanLimits {
  maxRows: number;
  pagination: boolean;
}

export interface ExecutionPlan {
  version: string;
  intent: string;
  entities: string[];
  operations: PlanOperation[];
  limits: PlanLimits;
}
```

`study-completion-result.model.ts`:
```typescript
export interface StudyCompletionResult {
  studyId: string;
  studyCode: string;
  customer: string;
  plannedCompletionDate: string | null;
  actualCompletionDate: string | null;
  classification: 'On Time' | 'Delayed' | 'Indeterminate';
  reason: string;
  dataQualityFlags: string[];
}
```

`assistant-query-response.model.ts`:
```typescript
import { ExecutionPlan } from './execution-plan.model';
import { StudyCompletionResult } from './study-completion-result.model';

export interface ValidationCheck { name: string; status: string; }
export interface ValidationResult { status: string; checks: ValidationCheck[]; errors: string[]; }
export interface QuerySummary { onTime: number; delayed: number; indeterminate: number; }

export interface AssistantQueryResponse {
  planId: string;
  traceId: string;
  status: string;
  markdownPlan: string;
  jsonPlan: ExecutionPlan;
  validation: ValidationResult;
  summary: QuerySummary;
  results: StudyCompletionResult[];
  message: string;
}
```

**Why typed models matter:**
If you leave `response` typed as `any`, TypeScript cannot catch a typo like
`response.sumarry.onTime` at build time — it only blows up at runtime in the browser.
Typed models give you autocomplete and catch mistakes before the user sees them.

### 13.5 Create the API Service

`src/app/features/insight-assistant/services/insight-assistant-api.service.ts`:
```typescript
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AssistantQueryResponse } from '../models/assistant-query-response.model';

@Injectable({ providedIn: 'root' })
export class InsightAssistantApiService {
  constructor(private http: HttpClient) {}

  query(query: string): Observable<AssistantQueryResponse> {
    return this.http.post<AssistantQueryResponse>('/api/assistant/query', {
      query,
      userContext: {
        userId: 'demo-user',
        roles: ['StudyViewer', 'CoreLabsViewer'],
        legalEntities: ['EU', 'US']
      }
    });
  }
}
```

**Why `http.post<AssistantQueryResponse>()` not just `http.post()`?**
Without the generic type parameter, `post()` returns `Observable<Object>`.
The subscribe callback parameter `r` becomes type `Object` — TypeScript cannot
know what fields it has, so it flags `r => this.response = r` as
`TS7006: Parameter 'r' implicitly has an 'any' type`.
Adding `<AssistantQueryResponse>` tells TypeScript exactly what the response
shape is, giving you type safety all the way from HTTP call to template.

**Concept — Angular Services and `HttpClient`:**
A service is a class that holds logic shared across components. `HttpClient` makes
HTTP calls to the backend. `@Injectable({ providedIn: 'root' })` registers it with
Angular's DI container as a singleton — available everywhere in the app.

### 13.6 Create the Component

You need three files for this component. Create all three before building.

First, create the **empty stylesheet** — the component references it and the build
fails if it doesn't exist, even if it has no content yet:

`src/app/features/insight-assistant/insight-assistant.component.scss`:
```scss
/* add component styles here */
```

Now create the **TypeScript class**:

`src/app/features/insight-assistant/insight-assistant.component.ts`:
```typescript
import { Component } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { NgFor, NgIf, JsonPipe } from '@angular/common';
import { InsightAssistantApiService } from './services/insight-assistant-api.service';
import { AssistantQueryResponse } from './models/assistant-query-response.model';

@Component({
  selector: 'app-insight-assistant',
  templateUrl: './insight-assistant.component.html',
  styleUrls: ['./insight-assistant.component.scss'],
  imports: [ReactiveFormsModule, NgFor, NgIf, JsonPipe]
})
export class InsightAssistantComponent {
  examples = [
    'Find studies not completed on time',
    'Show delayed studies',
    'Show indeterminate studies',
    'Show completed late studies'
  ];

  response: AssistantQueryResponse | null = null;
  form: FormGroup;

  get queryControl(): FormControl { return this.form.get('query') as FormControl; }

  constructor(private fb: FormBuilder, private api: InsightAssistantApiService) {
    // form must be initialised here, not as a class field initialiser.
    // Class fields run before Angular fills in constructor-injected values,
    // so `this.fb` would be undefined at field initialisation time (TS2729).
    this.form = this.fb.group({ query: ['Find studies not completed on time'] });
  }

  runQuery(): void {
    this.api.query(this.form.value.query || '').subscribe(
      (r: AssistantQueryResponse) => { this.response = r; }
    );
  }

  setQuery(q: string): void { this.form.patchValue({ query: q }); }
}
```

**Three important patterns explained:**

**1. Standalone `imports` array**
`imports: [ReactiveFormsModule, NgFor, NgIf, JsonPipe]` inside `@Component` replaces
the old NgModule declaration. The component is self-contained — it declares everything
it needs, making it easy to move or reuse.

**2. `queryControl` getter (fixes TS2739 / TS4111)**
`[formControl]="form.controls.query"` fails in strict TypeScript because
`FormGroup.controls` returns `AbstractControl`, which is missing properties
that `[formControl]` requires (`FormControl`-specific members). It also triggers
`TS4111` because the property comes from an index signature.
The getter returns an explicitly typed `FormControl`, satisfying both errors:
```typescript
get queryControl(): FormControl { return this.form.get('query') as FormControl; }
```

**3. `form` initialised in constructor (fixes TS2729)**
Class field initialisers (`form = this.fb.group(...)`) run **before** Angular
populates constructor parameters, so `this.fb` is `undefined` at that point.
Moving the assignment into the constructor body runs it **after** Angular has
injected `fb`.

`src/app/features/insight-assistant/insight-assistant.component.html`:
```html
<h1>eLIMS Insight Assistant</h1>
<h3>Governed Natural-Language Analytics</h3>

<div>
  <label>Ask eLIMS</label>
  <input [formControl]="queryControl" />
  <button (click)="runQuery()">Run Query</button>
</div>

<div>
  <button *ngFor="let ex of examples" (click)="setQuery(ex)">{{ ex }}</button>
</div>

<div *ngIf="response">
  <h4>Summary</h4>
  <p>On Time: {{response.summary.onTime}} | Delayed: {{response.summary.delayed}} | Indeterminate: {{response.summary.indeterminate}}</p>
  <h4>Results</h4>
  <pre>{{ response.results | json }}</pre>
  <h4>Generated Plan</h4>
  <pre>{{ response.markdownPlan }}</pre>
  <h4>JSON Execution Plan</h4>
  <pre>{{ response.jsonPlan | json }}</pre>
</div>
```

**Concept — Angular Data Binding:**
- `[formControl]="queryControl"` — binds the input to the typed form control
- `(click)="runQuery()"` — calls a method when the button is clicked
- `{{response.summary.onTime}}` — displays a value; no `?.` needed here because
  the whole block is inside `*ngIf="response"` which guarantees `response` is non-null
- `*ngFor="let ex of examples"` — loops and creates one button per item
- `*ngIf="response"` — only shows this section when response is not null
- `| json` — pipe: pretty-prints an object as JSON

### 13.7 Install dependencies

```bash
cd elims-insight-assistant/frontend/elims-insight-assistant-ui
npm install
```

This installs Angular 21, RxJS, zone.js, and the esbuild build tooling into `node_modules/`.
`node_modules/` is listed in `.gitignore` and is never committed — every developer runs
`npm install` after cloning.

> **If `ng build` fails with `Could not resolve "zone.js"`** even after `npm install`,
> zone.js was not picked up correctly. Install it explicitly:
> ```bash
> npm install zone.js --save
> ```
> This adds it to `node_modules/` and updates `package.json`. Then retry the build.

### 13.8 Proxy for local development

When Angular runs on port 4200 and the API runs on port 5000, the browser blocks
cross-origin requests. The proxy forwards Angular's `/api` calls to the backend
so the browser never sees a cross-origin request.

**Step 1 — Create `proxy.conf.json`** at the project root (same level as `angular.json`,
NOT inside `src/`):
```json
{
  "/api": {
    "target": "http://localhost:5000",
    "secure": false,
    "changeOrigin": true
  }
}
```

**Step 2 — Wire it into `angular.json`**

`proxyConfig` belongs in the **`serve`** section, not the `build` section.
`ng new` generates both sections — find `serve` and add `proxyConfig` to its `options`:

```json
"architect": {
  "build": {
    "builder": "@angular/build:application",
    "options": {
      "outputPath": "dist/elims-insight-assistant-ui",
      "index": "src/index.html",
      "browser": "src/main.ts",
      "polyfills": ["zone.js"],
      "tsConfig": "tsconfig.app.json",
      "styles": ["src/styles.scss"],
      "scripts": []
    },
    ...
  },
  "serve": {
    "builder": "@angular/build:dev-server",
    "options": {
      "proxyConfig": "proxy.conf.json"    ← ADD THIS HERE, inside serve not build
    },
    "configurations": {
      "production": {
        "buildTarget": "elims-insight-assistant-ui:build:production"
      },
      "development": {
        "buildTarget": "elims-insight-assistant-ui:build:development"
      }
    },
    "defaultConfiguration": "development"
  }
}
```

> **Common mistake:** `ng new` puts `proxyConfig` in `build > options` if you add it
> there by accident. The proxy only applies to `ng serve`, so it must live in
> `serve > options`. Putting it in `build` has no effect and is silently ignored.

**Step 3 — Remove the `assets` entry pointing to `public/`**

`ng new` adds an assets block that copies everything from a `public/` directory.
That directory does not exist in this project. Delete these lines from `build > options`
or the build will fail on a clean checkout:

```json
// DELETE THIS BLOCK from build > options:
"assets": [
  {
    "glob": "**/*",
    "input": "public"
  }
],
```

### 13.9 Build and run the frontend

**Build once (check for compile errors):**
```bash
npx ng build --configuration development
```
A clean build outputs something like:
```
✔ Building...
main.js      | 1.29 MB
polyfills.js | 92.95 kB
styles.css   | 156 bytes
Application bundle generation complete.
```
Zero errors means all TypeScript and template type checks passed.

**Run with live reload:**
```bash
npx ng serve
```
Open http://localhost:4200 in your browser.
The `ng serve` output shows:
```
✔ Building...
Watch mode enabled. Watching for file changes...
  ➜  Local: http://localhost:4200/
```
Any file change triggers an instant rebuild without restarting the server.

---

## Part 14 — Configure OpenAI and Run Everything

### Step 14.1 — Get an OpenAI API Key

> **Skip this step if you just want to run the demo in mock mode.**
> Mock mode works without any account — jump straight to Step 14.2.

To enable real NL intent extraction (queries like "which studies missed their deadline?"):

**1. Create an OpenAI account**
Go to https://platform.openai.com and click **Sign up**.
Use a Google/Microsoft account or create one with your email.

**2. Add billing (required to use the API)**
Go to https://platform.openai.com/settings/organization/billing
Click **Add payment method** and add a card.
You only pay for what you use. `gpt-4o-mini` costs roughly **$0.15 per 1 million input tokens**.
A single query uses ~300 tokens — so $5 of credit lasts thousands of queries.

**3. Create your API key**
Go to https://platform.openai.com/api-keys
Click **Create new secret key** → give it a name (e.g. "elims-dev") → click **Create**.
Copy the key immediately — it starts with `sk-proj-...` and is only shown once.

> **Never commit your API key to git.** Anyone with your key can use your billing account.
> The methods below keep the key out of your code.

**4. Add the key to the project — two options:**

**Option A — User Secrets (recommended: key never touches any file)**
```bash
cd elims-insight-assistant/backend/src/ElimsInsightAssistant.Api
dotnet user-secrets set "OpenAI:ApiKey" "sk-proj-..."
```
The key is stored in your OS user profile, not in the project folder.
It is never included when you share or push the code.

> **If you see: `Could not find the global property 'UserSecretsId'`**
> The `.csproj` file is missing the `<UserSecretsId>` property. It is already
> present in this repository's `.csproj`, but if you created your own project
> you need to add it inside `<PropertyGroup>`:
> ```xml
> <UserSecretsId>elims-insight-assistant-api</UserSecretsId>
> ```
> The value can be any unique string — a project name works fine. Save the file
> then re-run `dotnet user-secrets set`.

**Option B — Environment variable**
```bash
# Mac / Linux
export OpenAI__ApiKey=sk-proj-...

# Windows Command Prompt
set OpenAI__ApiKey=sk-proj-...
```
> Note the double underscore `__` — that is how .NET maps nested config (`OpenAI:ApiKey`) to env vars.

**How the app tells you which mode is active:**
Every time the API starts, it logs one line before anything else:
```
# With a key:
info: Plan generator: OpenAiPlanGenerator (gpt-4o-mini, structured outputs)

# Without a key:
warn: Plan generator: MockPlanGenerator (keyword matching only — no real NLP).
      To enable real NL intent extraction set OpenAI:ApiKey.
      See docs/build-from-scratch.md §14.1 for how to obtain and configure a key.
```
If you see the `warn` line, you are in mock mode. The warning is intentional — there is no silent fallback.

**How the app chooses which generator to use:**
```
OpenAI:ApiKey present and non-empty?
  YES → OpenAiPlanGenerator  (real NLP via gpt-4o-mini, strict JSON schema)
  NO  → MockPlanGenerator    (keyword matching, no API call, no cost)
```

With `OpenAiPlanGenerator` active, queries like these all work even though
they were never hardcoded:
- *"Which studies missed their deadline?"*
- *"Show me overdue trials"*
- *"Find studies that finished after the planned date"*

### Step 14.2 — Build and run the backend

Open a terminal (Terminal 1) and navigate to the API project:

```bash
cd elims-insight-assistant/backend/src/ElimsInsightAssistant.Api
```

**Step A — Restore packages**

This downloads all NuGet packages (OpenAI SDK, Swashbuckle, etc.) listed in the
`.csproj` file into a local cache. Only needed once after cloning or adding a new package:

```bash
dotnet restore
```

Expected output:
```
Restored .../ElimsInsightAssistant.Api.csproj
```

**Step B — Build**

Compiles all C# source files and checks for errors:

```bash
dotnet build
```

Expected output:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

If you see errors here, fix them before continuing — `dotnet run` will also fail.

**Step C — Run**

Starts the API web server:

```bash
dotnet run --urls http://localhost:5000
```

Expected startup output:
```
warn: Program[0]
      Plan generator: MockPlanGenerator (keyword matching only — no real NLP).
      To enable real NL intent extraction set OpenAI:ApiKey.
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

> The `warn` line on startup is intentional — it tells you the app is in mock mode.
> It is not an error. See Step 14.1 to enable real NLP with an OpenAI key.

**Step D — Verify the backend is working**

Open your browser and go to:
```
http://localhost:5000/swagger
```

You should see the Swagger UI listing all API endpoints. This confirms the backend
is running and reachable. You can also test from the terminal:

```bash
curl -X POST http://localhost:5000/api/assistant/query \
  -H "Content-Type: application/json" \
  -d "{\"query\":\"Show delayed studies\",\"userContext\":{\"userId\":\"demo-user\",\"roles\":[\"StudyViewer\",\"CoreLabsViewer\"],\"legalEntities\":[\"EU\",\"US\"]}}"
```

Expected: a JSON response with `"status": "Completed"` and results.

> **Windows PowerShell note:** curl in PowerShell behaves differently. Use this instead:
> ```powershell
> Invoke-RestMethod -Uri http://localhost:5000/api/assistant/query `
>   -Method Post `
>   -ContentType "application/json" `
>   -Body '{"query":"Show delayed studies","userContext":{"userId":"demo-user","roles":["StudyViewer","CoreLabsViewer"],"legalEntities":["EU","US"]}}'
> ```

### Step 14.3 — Run the frontend

Open a **second terminal** (keep the backend running in Terminal 1):

```bash
cd elims-insight-assistant/frontend/elims-insight-assistant-ui
npm install       # first time only
npx ng serve
```

Expected output:
```
✔ Building...
Watch mode enabled. Watching for file changes...
  ➜  Local: http://localhost:4200/
```

Open `http://localhost:4200` in your browser. You should see the eLIMS Insight
Assistant page with a query input and four example buttons.

### Step 14.4 — Verify the frontend and backend are connected

**How the connection works:**

```
Browser (port 4200)
    │
    │  POST /api/assistant/query
    ▼
Angular dev server (ng serve)
    │
    │  proxy.conf.json forwards /api/* to http://localhost:5000
    ▼
.NET backend (port 5000)
    │
    │  Returns JSON response
    ▼
Angular dev server
    │
    ▼
Browser renders results
```

The browser never talks to port 5000 directly. The Angular dev server acts as a
middleman, forwarding any request starting with `/api` to the backend. This avoids
cross-origin (CORS) browser errors during development.

**Verify the connection:**

1. Make sure both terminals are running (backend on 5000, frontend on 4200)
2. Open `http://localhost:4200`
3. Click **Run Query**
4. The Summary section should appear with: `On Time: 2 | Delayed: 1 | Indeterminate: 1`

If the Summary appears — frontend and backend are connected correctly. ✓

**If nothing appears after clicking Run Query:**

| Symptom | Cause | Fix |
|---|---|---|
| Browser console shows `502 Bad Gateway` | Backend not running | Start `dotnet run` in Terminal 1 |
| Browser console shows `ERR_CONNECTION_REFUSED` | Wrong port in proxy | Check `proxy.conf.json` targets port 5000 |
| Page loads but results are empty | Role/entity mismatch | The service sends `StudyViewer` + `CoreLabsViewer` + `EU,US` — check `insight-assistant-api.service.ts` |
| `ng serve` terminal shows `ECONNREFUSED 127.0.0.1:5000` | Backend stopped | Restart `dotnet run` |

### Step 14.5 — Run the tests

Open a **third terminal**:

```bash
cd elims-insight-assistant/backend/src/ElimsInsightAssistant.Tests
dotnet test --verbosity normal
```

Expected output:
```
Passed! - Failed: 0, Passed: 17, Skipped: 0, Total: 17
```

All 17 tests should pass. Tests do not require the backend to be running — they
run against the code directly in-process.

---

## Part 15 — Testing Both Modes and Switching Providers

### 15.1 — How the two modes differ

| | Mock mode (no key) | Real mode (OpenAI key set) |
|---|---|---|
| **How it works** | Keyword matching in code | GPT-4o-mini generates a structured JSON plan |
| **Startup log** | `warn: MockPlanGenerator` | `info: OpenAiPlanGenerator` |
| **Cost** | Free — no API call made | ~$0.15 per million tokens (~300 tokens per query) |
| **Queries that work** | Only the 4 hardcoded phrases | Any natural language query |
| **Response time** | ~5 ms | ~1–3 seconds |
| **Fails with** | Unrecognised query → `UnsupportedQuery` | Bad key → 503; no credits → 429 |
| **Good for** | Local dev, CI, demos | Real NLP testing, production |

### 15.2 — How to test mock mode

Mock mode requires no key — just make sure no `OpenAI:ApiKey` is set:

```bash
# Confirm no key is stored in User Secrets
cd elims-insight-assistant/backend/src/ElimsInsightAssistant.Api
dotnet user-secrets list
```

If the key appears, remove it:

```bash
dotnet user-secrets remove "OpenAI:ApiKey"
```

Start the backend — you must see the `warn` line:

```
warn: Plan generator: MockPlanGenerator (keyword matching only — no real NLP).
```

**Test queries that work in mock mode** (exact phrases, case-insensitive):

```
Find studies not completed on time  ✓
Show delayed studies                ✓
Show indeterminate studies          ✓
Show completed late studies         ✓
```

**Test that unrecognised queries are handled gracefully:**

```powershell
Invoke-RestMethod -Uri http://localhost:5000/api/assistant/query `
  -Method Post -ContentType "application/json" `
  -Body '{"query":"something random","userContext":{"userId":"u1","roles":["StudyViewer","CoreLabsViewer"],"legalEntities":["EU"]}}'
```

Expected response: `"status": "UnsupportedQuery"` — not a crash, not a 500.

### 15.3 — How to test real (OpenAI) mode

Set the key, restart, confirm the `info` line appears:

```bash
dotnet user-secrets set "OpenAI:ApiKey" "sk-proj-..."
dotnet run --urls http://localhost:5000
```

Expected startup:
```
info: Plan generator: OpenAiPlanGenerator (gpt-4o-mini, structured outputs)
```

**Test with free-text queries that mock mode cannot handle:**

```
Which studies missed their deadline?
Show me overdue trials
Find studies that finished after the planned date
Any studies with missing completion data?
```

All of these should return `"status": "Completed"` with real results — the LLM
extracts the intent and maps it to the same execution plan structure.

**Test the 503 path (transient error handling):**

Set an invalid key to force an auth failure:
```bash
dotnet user-secrets set "OpenAI:ApiKey" "sk-invalid"
dotnet run --urls http://localhost:5000
```

Send any query — you should get HTTP 503:
```json
{ "status": "ServiceUnavailable", "message": "Plan generation service is temporarily unavailable. Please try again." }
```

The full error (401 from OpenAI) is in the server terminal, not in the response.
Reset to your real key or remove it to restore normal operation.

### 15.4 — How to switch between modes at runtime

You do not need to change any code or rebuild. Just set or remove the secret and restart:

```bash
# Switch TO real mode
dotnet user-secrets set "OpenAI:ApiKey" "sk-proj-..."

# Switch TO mock mode
dotnet user-secrets remove "OpenAI:ApiKey"

# Then restart the backend — mode is chosen once at startup
dotnet run --urls http://localhost:5000
```

The startup log always tells you which mode is active — there is no silent fallback.

### 15.5 — How to migrate to a different LLM provider

The app is designed so only one file needs to change to swap providers:
`Services/PlanGenerator.cs`.

The interface `IPlanGenerator` stays the same regardless of provider:

```csharp
public interface IPlanGenerator {
    Task<PlanGeneratorResult> GenerateAsync(string query);
}
```

**To add Anthropic Claude as an alternative:**

1. Add the Anthropic SDK:
   ```bash
   dotnet add package Anthropic.SDK
   ```

2. Create a new class `AnthropicPlanGenerator : IPlanGenerator` in `PlanGenerator.cs`
   using the same structure as `OpenAiPlanGenerator` — prompt, parse JSON, return
   `PlanGeneratorResult`.

3. Update `Program.cs` to check for an Anthropic key and register accordingly:
   ```csharp
   var anthropicKey = builder.Configuration["Anthropic:ApiKey"];
   var openAiKey    = builder.Configuration["OpenAI:ApiKey"];

   if (!string.IsNullOrWhiteSpace(openAiKey))
       builder.Services.AddSingleton<IPlanGenerator, OpenAiPlanGenerator>();
   else if (!string.IsNullOrWhiteSpace(anthropicKey))
       builder.Services.AddSingleton<IPlanGenerator, AnthropicPlanGenerator>();
   else
       builder.Services.AddSingleton<IPlanGenerator, MockPlanGenerator>();
   ```

4. Store the key:
   ```bash
   dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."
   ```

The controller, validator, execution engine, and frontend all stay unchanged —
they only know about `IPlanGenerator`, not which provider is behind it.

**Provider comparison for this use case:**

| Provider | Model to use | Structured outputs | Free tier |
|---|---|---|---|
| OpenAI (current) | gpt-4o-mini | `CreateJsonSchemaFormat` (strict) | No — $5 min |
| Anthropic Claude | claude-haiku-4-5 | Tool use / JSON mode | No — $5 min |
| Google Gemini | gemini-2.0-flash | Response schema | Yes — free tier available |

Gemini is worth considering if you want a free API for development — the free tier
handles hundreds of queries per day at no cost.

---

## Part 16 — UI Screens

> These show what you see at `http://localhost:4200` after running `npm install && npx ng serve`
> with the backend running at `http://localhost:5000`.

### Screen 1 — Initial page (app loads)

```
┌─────────────────────────────────────────────────────────────────┐
│  eLIMS Insight Assistant                                        │
│  Governed Natural-Language Analytics                            │
│                                                                 │
│  Ask eLIMS  [Find studies not completed on time      ] [Run]   │
│                                                                 │
│  [Find studies not completed on time]  [Show delayed studies]  │
│  [Show indeterminate studies]  [Show completed late studies]   │
└─────────────────────────────────────────────────────────────────┘
```

**What you see:**
- A text input pre-filled with "Find studies not completed on time"
- A **Run Query** button that calls the backend
- Four example buttons — clicking any one sets the input and lets you run it

### Screen 2 — After clicking Run Query

The backend returns results within ~200 ms (mock mode) and the page expands:

```
┌─────────────────────────────────────────────────────────────────┐
│  eLIMS Insight Assistant                                        │
│  Governed Natural-Language Analytics                            │
│                                                                 │
│  Ask eLIMS  [Find studies not completed on time      ] [Run]   │
│                                                                 │
│  [Find studies not completed on time]  [Show delayed studies]  │
│  [Show indeterminate studies]  [Show completed late studies]   │
│                                                                 │
│  ── Summary ───────────────────────────────────────────────── │
│  On Time: 2 | Delayed: 1 | Indeterminate: 1                    │
│                                                                 │
│  ── Results ───────────────────────────────────────────────── │
│  [                                                              │
│    {                                                            │
│      "studyId": "S2",                                           │
│      "studyCode": "ST-002",                                     │
│      "customer": "XYZ Labs",                                    │
│      "plannedCompletionDate": "2026-04-15T00:00:00",           │
│      "actualCompletionDate": "2026-04-17T09:30:00Z",           │
│      "classification": "Delayed",                               │
│      "reason": "Actual completion date is 2 days after         │
│                 planned completion date.",                       │
│      "dataQualityFlags": []                                     │
│    },                                                           │
│    {                                                            │
│      "studyId": "S4",                                           │
│      "studyCode": "ST-004",                                     │
│      "customer": "Delta Bio",                                   │
│      "plannedCompletionDate": null,                             │
│      "actualCompletionDate": null,                              │
│      "classification": "Indeterminate",                         │
│      "reason": "Planned completion date or completed TestP      │
│                 timestamp is missing.",                         │
│      "dataQualityFlags": [                                      │
│        "missing_planned_completion_date",                       │
│        "no_completed_testps"                                    │
│      ]                                                          │
│    }                                                            │
│  ]                                                              │
│                                                                 │
│  ── Generated Plan ────────────────────────────────────────── │
│  # Analysis Plan                                                │
│  Intent: Find studies not completed on time.                    │
│  ## Steps                                                       │
│  1. Fetch studies from Study Service.                           │
│  2. Fetch completed TestPs from CoreLabs Service.               │
│  3. Correlate Study and TestP records using studyId.            │
│  ...                                                            │
│                                                                 │
│  ── JSON Execution Plan ───────────────────────────────────── │
│  { "version": "1.0", "intent": "find_studies_not_completed_   │
│    on_time", "entities": ["study", "testp"], ...  }             │
└─────────────────────────────────────────────────────────────────┘
```

**What each section means:**

| Section | What it shows |
|---|---|
| **Summary** | Aggregate count of all studies across all three classifications |
| **Results** | Only the studies matching the query intent (e.g. Delayed + Indeterminate) |
| **Generated Plan** | Human-readable markdown explaining what the system decided to do |
| **JSON Execution Plan** | Machine-readable plan — the structured JSON the backend executed |

### Screen 3 — Clicking an example button ("Show delayed studies")

Clicking the button fills the input and you click Run. The summary counts are the same
(all 4 seed studies are always evaluated), but the intent and results may differ when
running with a real OpenAI key:

```
  On Time: 2 | Delayed: 1 | Indeterminate: 1

  Results: [ { "studyCode": "ST-002", "classification": "Delayed", ... } ]
```

> **Mock mode note:** In mock mode (no API key), all four example queries map to the
> same keyword-matched plan and produce identical results. With a real OpenAI key,
> "Show delayed studies" produces a plan with `includeClassifications: ["Delayed"]`
> only, filtering out the Indeterminate result.

### How the data flows (UI → API → UI)

```
User types query
      │
      ▼
[Run Query] clicked
      │
      ▼
InsightAssistantApiService.query()
  POST /api/assistant/query
  Body: { query, userContext: { roles, legalEntities } }
      │
      ▼
Backend: PlanGenerator → Validator → ExecutionEngine → Response
      │
      ▼
AssistantQueryResponse arrives
  { status, summary, results, markdownPlan, jsonPlan, validation }
      │
      ▼
Component binds response to template
  *ngIf="response" reveals Summary + Results + Plan sections
```

---

## Part 17 — Key Concepts Summary

| Concept | What It Means | Where Used |
|---|---|---|
| **Separation of Concerns** | Each class does one thing | Folder structure |
| **Interface + Implementation** | Define contract separately from code | All services |
| **Dependency Injection** | Framework wires up objects for you | Program.cs, constructors |
| **Allowlist Validation** | Only approved items pass; everything else fails | PlanValidator |
| **LINQ** | Query collections like a language | ExecutionEngine |
| **async/await** | Don't block the server while waiting for I/O | Services, controllers |
| **Records** | Immutable data containers | All models |
| **Singleton vs Scoped** | Lifetime of objects in the DI container | Program.cs |
| **ConcurrentDictionary** | Thread-safe dictionary for multi-request environments | AuditService |
| **xUnit [Fact]** | Mark a method as a test | All test classes |
| **Angular Component** | A reusable UI block with HTML + TypeScript | InsightAssistantComponent |
| **Standalone Component** | Component that declares its own imports — no NgModule needed | Angular 17+ default; `imports: [...]` inside `@Component` |
| **Angular Service** | Shared logic injected into components | InsightAssistantApiService |
| **Data Binding** | Connect UI to code automatically | Component HTML template |
| **`queryControl` getter** | Typed accessor for a form field — converts `AbstractControl` to `FormControl` to satisfy strict template type checking | `get queryControl(): FormControl` |
| **Generic `http.post<T>()`** | Tells TypeScript the shape of the HTTP response — prevents implicit `any` on subscribe callbacks | `http.post<AssistantQueryResponse>(...)` |
| **Proxy Config** | Forward Angular dev requests to the API | proxy.conf.json |
| **OpenAI ChatClient** | Send a prompt, get a structured response | OpenAiPlanGenerator |
| **Structured Outputs** | Schema-constrained JSON enforced by provider, not just prompted | `CreateJsonSchemaFormat` |
| **`IsServerError` flag** | Distinguish retryable failures from unsupported queries | PlanGeneratorResult |
| **Log server-side, generic to client** | Never return raw exception text to API consumers | OpenAiPlanGenerator catch blocks |
| **User Secrets** | Store API keys locally without committing them to git | OpenAI:ApiKey config |
| **Feature Flag via Config** | Switch implementations at startup based on config | Program.cs key check |

---

## Common Mistakes and How to Avoid Them

**"Could not find seed-data/studies.json"**
The path in `DemoDataServices.cs` uses `..` to go up from `ContentRootPath`.
Count carefully: from `.../ElimsInsightAssistant.Api`, three `..` reaches
`elims-insight-assistant/` where `seed-data/` lives. Four `..` overshoots.

**"Port 5000 is already in use"**
Another process is using the port. In your terminal run:
- Windows: `netstat -ano | findstr :5000` then `taskkill /PID <number> /F`
- Mac/Linux: `lsof -i :5000` then `kill <PID>`

**"UnauthorizedAccessException: User lacks required roles"**
Your request body must include both `"StudyViewer"` and `"CoreLabsViewer"` in roles.

**"No results returned"**
Check `legalEntities` in your request. The EU seed data requires `"EU"` in the list.
S3 (BioTest) is a US entity and will only appear if `"US"` is included.

**`NG2008: Could not find stylesheet file './insight-assistant.component.scss'`**
The component's `styleUrls` references a `.scss` file that must exist on disk even
if it has no content. Create it manually:
```
src/app/features/insight-assistant/insight-assistant.component.scss
```
Leave it empty or add a comment — the build just needs the file to be present.

**`TS2307: Cannot find module './execution-plan.model'`**
The file `execution-plan.model.ts` is missing or empty. Create it with the full
`ExecutionPlan` interface content from §13.4. An empty file is not enough —
the TypeScript compiler needs the exported interfaces inside it.

**`Could not resolve "zone.js"` after npm install**
Run `npm install zone.js --save` explicitly. Sometimes `npm install` reports
"up to date" but zone.js was not in `package.json` and so was never installed.

**Angular blank page**
Ensure both the backend (`dotnet run`) and frontend (`npx ng serve`) are running.
Check the browser console (F12) for error messages. Common causes:
- Backend not running → proxy returns 502; check `dotnet run` terminal
- `npm install` not run → `node_modules/` missing; run it first
- Wrong port → proxy targets `http://localhost:5000`; confirm backend listens there

**`TS7006: Parameter 'r' implicitly has an 'any' type`**
The `http.post()` call is missing its generic type parameter.
Change `this.http.post(...)` to `this.http.post<AssistantQueryResponse>(...)`.
Without it, the response is typed as `Object` and the subscribe callback is untyped.

**`TS2729: Property used before its initialization`**
`this.fb.group(...)` is used as a class field initialiser but `fb` is injected in
the constructor — at field-init time it is still `undefined`.
Move `this.form = this.fb.group(...)` inside the constructor body.

**`TS2739 / TS4111` on `form.controls.query` in template**
`FormGroup.controls` returns `AbstractControl` which lacks `FormControl`-specific
members. Add a getter:
```typescript
get queryControl(): FormControl { return this.form.get('query') as FormControl; }
```
Then use `[formControl]="queryControl"` in the template.

**API returns HTTP 503 "Plan generation service is temporarily unavailable"**
This is a transient OpenAI failure (network, provider outage, invalid API key).
Check: API key is correct → has credits → internet is reachable.
This is intentional — 503 tells clients to retry, unlike 200 UnsupportedQuery which they should not retry.
Full error details are in the server log (`dotnet run` terminal output), not in the HTTP response.

**"AI plan generation failed" appears in server logs but not in the API response**
This is by design — raw exception messages are never sent to clients (security + stable contracts).
Look at the terminal where `dotnet run` is running to see the full exception.

**"OpenAI:ApiKey is not configured" at startup**
This only happens if `OpenAiPlanGenerator` is registered but the key is missing.
The app normally falls back to `MockPlanGenerator` when the key is empty.
Check `Program.cs` — the key check uses `string.IsNullOrWhiteSpace`.

**`Could not find the global property 'UserSecretsId'` when running `dotnet user-secrets set`**
The `.csproj` file is missing `<UserSecretsId>`. Add it inside `<PropertyGroup>`:
```xml
<UserSecretsId>elims-insight-assistant-api</UserSecretsId>
```
Save the `.csproj` then re-run `dotnet user-secrets set`.

---

*This document covers the complete implementation of eLIMS Insight Assistant.
Every decision — from folder layout to OpenAI JSON mode — exists for a reason.
Understanding the why makes the what memorable.*
