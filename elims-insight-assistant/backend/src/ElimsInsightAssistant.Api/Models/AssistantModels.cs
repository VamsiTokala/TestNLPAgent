using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElimsInsightAssistant.Api.Models;

/// <summary>
/// Accepts any JSON value for a string-list field: null → [], string → [value], array-of-strings → as-is.
/// Needed because some AI providers (e.g. OpenRouter) occasionally return "entities" as a plain string.
/// </summary>
internal sealed class FlexibleStringListConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return [];
            case JsonTokenType.String:
                return [reader.GetString() ?? string.Empty];
            case JsonTokenType.StartArray:
                var list = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    list.Add(reader.TokenType == JsonTokenType.String
                        ? reader.GetString() ?? string.Empty
                        : JsonSerializer.Deserialize<JsonElement>(ref reader).GetRawText());
                return list;
            default:
                reader.Skip();
                return [];
        }
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var s in value) writer.WriteStringValue(s);
        writer.WriteEndArray();
    }
}

public record NaturalLanguageQueryRequest(string Query, UserContext UserContext, string? Provider = null);
public record UserContext(string UserId, List<string> Roles, List<string> LegalEntities);

public record AssistantQueryResponse
{
    public string PlanId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string Status { get; init; } = "Completed";
    public string PlanGeneratorMode { get; init; } = string.Empty;
    public string MarkdownPlan { get; init; } = string.Empty;
    public ExecutionPlan JsonPlan { get; init; } = new();
    public ValidationResult Validation { get; init; } = new();
    public QuerySummary Summary { get; init; } = new();
    public List<Dictionary<string, object?>> Results { get; init; } = [];
    public Dictionary<string, List<Dictionary<string, object?>>> Datasets { get; init; } = [];
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
    [JsonConverter(typeof(FlexibleStringListConverter))]
    public List<string> Entities { get; init; } = [];
    // Which contract's rows the plan is ultimately about (e.g. the entity being
    // counted, listed, or classified). Empty means "default to the side that
    // owns plannedCompletionDate / the first operation's service".
    public string PrimaryEntity { get; init; } = string.Empty;
    public List<PlanOperation> Operations { get; init; } = [];
    public PlanCorrelate Correlate { get; init; } = new();
    public PlanTransform Transform { get; init; } = new();
    public PlanClassificationRules Classify { get; init; } = new();
    public PlanOutput Output { get; init; } = new();
    public PlanLimits Limits { get; init; } = new(500, true);
}

public record PlanOperation(string Service, string Action, List<string> Select, List<PlanFilter> Filters, string? Reason = null);
public record PlanFilter(string Field, string Op, string? Value);
public record PlanCorrelate(string LeftEntity = "", string RightEntity = "", string LeftField = "", string RightField = "");
public record PlanTransform(List<string>? GroupBy = null, List<PlanAggregate>? Aggregates = null)
{
    public List<string> GroupBy { get; init; } = GroupBy ?? [];
    public List<PlanAggregate> Aggregates { get; init; } = Aggregates ?? [];
}
public record PlanAggregate(string Field, string Fn, string As);
public record PlanClassificationRules(string OnTime = "actualCompletionDate <= plannedCompletionDate", string Delayed = "actualCompletionDate > plannedCompletionDate", string Indeterminate = "plannedCompletionDate is null OR actualCompletionDate is null");
public record PlanOutput(List<string>? IncludeClassifications = null, List<string>? Columns = null)
{
    public List<string> IncludeClassifications { get; init; } = IncludeClassifications ?? [];
    public List<string> Columns { get; init; } = Columns ?? [];
}
public record PlanLimits(int MaxRows, bool Pagination);

public record StudyDto(string StudyId, string StudyCode, string Customer, string LegalEntity, DateTime? PlannedCompletionDate);
public record TestPDto(string TestpId, string StudyId, string Status, DateTime? CompletedAt, string RunType, string? Result);
public record ProtocolDto(string ProtocolId, string StudyId, string Version, string Status, DateTime? ApprovedAt, DateTime? ExpiresAt);
public record SampleDto(string SampleId, string StudyId, string SampleType, string Status, DateTime? CollectedAt, string CollectionSite);

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
    public List<Dictionary<string, object?>> ResultSnapshot { get; init; } = [];
}
