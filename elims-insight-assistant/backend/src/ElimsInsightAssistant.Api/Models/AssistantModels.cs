namespace ElimsInsightAssistant.Api.Models;

public record NaturalLanguageQueryRequest(string Query, UserContext UserContext);
public record UserContext(string UserId, List<string> Roles, List<string> LegalEntities);

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

public record QuerySummary(int OnTime = 0, int Delayed = 0, int Indeterminate = 0);
public record ValidationResult(string Status = "Passed", List<ValidationCheck>? Checks = null, List<string>? Errors = null)
{
    public List<ValidationCheck> Checks { get; init; } = Checks ?? [];
    public List<string> Errors { get; init; } = Errors ?? [];
}
public record ValidationCheck(string Name, string Status);

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
public record PlanCorrelate(string LeftEntity = "study", string RightEntity = "testp", string LeftField = "studyId", string RightField = "studyId");
public record PlanTransform(List<string>? GroupBy = null, List<PlanAggregate>? Aggregates = null)
{
    public List<string> GroupBy { get; init; } = GroupBy ?? ["studyId"];
    public List<PlanAggregate> Aggregates { get; init; } = Aggregates ?? [new("completedAt", "max", "actualCompletionDate")];
}
public record PlanAggregate(string Field, string Fn, string As);
public record PlanClassificationRules(string OnTime = "actualCompletionDate <= plannedCompletionDate", string Delayed = "actualCompletionDate > plannedCompletionDate", string Indeterminate = "plannedCompletionDate is null OR actualCompletionDate is null");
public record PlanOutput(List<string>? IncludeClassifications = null, List<string>? Columns = null)
{
    public List<string> IncludeClassifications { get; init; } = IncludeClassifications ?? ["Delayed", "Indeterminate"];
    public List<string> Columns { get; init; } = Columns ?? ["studyId", "studyCode", "customer", "plannedCompletionDate", "actualCompletionDate", "classification", "reason", "dataQualityFlags"];
}
public record PlanLimits(int MaxRows, bool Pagination);

public record StudyDto(string StudyId, string StudyCode, string Customer, string LegalEntity, DateTime? PlannedCompletionDate);
public record TestPDto(string TestpId, string StudyId, string Status, DateTime? CompletedAt, string RunType, string? Result);

public record StudyCompletionResult
{
    public string StudyId { get; init; } = string.Empty;
    public string StudyCode { get; init; } = string.Empty;
    public string Customer { get; init; } = string.Empty;
    public DateTime? PlannedCompletionDate { get; init; }
    public DateTime? ActualCompletionDate { get; init; }
    public string Classification { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public List<string> DataQualityFlags { get; init; } = [];
}

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
