using System.Reflection;
using System.Runtime.CompilerServices;
using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;

[assembly: InternalsVisibleTo("ElimsInsightAssistant.Tests")]

namespace ElimsInsightAssistant.Api.Execution;

public record ExecutionOutput(
    QuerySummary Summary,
    List<StudyCompletionResult> Rows,
    Dictionary<string, List<Dictionary<string, object?>>> Datasets,
    List<string> ServicesCalled);

public interface IExecutionEngine
{
    Task<ExecutionOutput> ExecuteAsync(ExecutionPlan plan, UserContext userContext);
}

public class ExecutionEngine : IExecutionEngine
{
    private readonly Dictionary<string, Func<Task<IEnumerable<object>>>> _dataSources;

    public ExecutionEngine(
        IStudyServiceClient studyClient,
        ICoreLabsServiceClient coreLabsClient,
        IProtocolServiceClient protocolClient,
        ISampleServiceClient sampleClient)
    {
        _dataSources = new(StringComparer.OrdinalIgnoreCase)
        {
            ["study-service"]    = async () => (await studyClient.ListStudiesAsync()).Cast<object>(),
            ["corelabs-service"] = async () => (await coreLabsClient.ListTestPsAsync()).Cast<object>(),
            ["protocol-service"] = async () => (await protocolClient.ListProtocolsAsync()).Cast<object>(),
            ["sample-service"]   = async () => (await sampleClient.ListSamplesAsync()).Cast<object>()
        };
    }

    public async Task<ExecutionOutput> ExecuteAsync(ExecutionPlan plan, UserContext userContext)
    {
        if (userContext.Roles.Count == 0)
            throw new UnauthorizedAccessException("User has no assigned roles.");

        // ── Fetch & filter every contract referenced in the plan ──────────────
        var rawByService = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in plan.Operations)
        {
            if (!_dataSources.TryGetValue(op.Service, out var fetch)) continue;
            var data = (await fetch()).Where(r => PassesFilters(r, op.Filters)).ToList();
            rawByService[op.Service] = data;
        }

        // ── Apply legal-entity authorization to any service whose records carry
        //    a legalEntity field (generic — works for any future contract too) ──
        foreach (var key in rawByService.Keys.ToList())
        {
            rawByService[key] = rawByService[key]
                .Where(obj =>
                {
                    var le = GetStringField(obj, "legalEntity");
                    return le is null || userContext.LegalEntities.Contains(le);
                })
                .ToList();
        }

        // ── Project raw data into dictionaries for the response ───────────────
        var datasets = rawByService.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Select(ToDict).ToList());

        // ── If this is a timeliness query, detect left/right services by field
        //    presence and run a generic classification flow ────────────────────
        var rows = new List<StudyCompletionResult>();
        var summary = new QuerySummary();

        var wantsClassification = plan.Output.IncludeClassifications.Count > 0;
        if (wantsClassification)
        {
            var aggregate = plan.Transform.Aggregates.FirstOrDefault()
                            ?? new PlanAggregate("completedAt", "max", "actualCompletionDate");

            // Left = service whose records have plannedCompletionDate
            // Right = a different service whose records have the aggregate source field
            var leftService  = FindServiceWithField(rawByService, "plannedCompletionDate");
            var rightService = FindServiceWithField(rawByService, aggregate.Field, excluding: leftService);

            if (leftService != null && rightService != null)
            {
                var leftRows  = rawByService[leftService];
                var rightRows = rawByService[rightService];

                var leftIdField  = !string.IsNullOrEmpty(plan.Correlate.LeftField)  ? plan.Correlate.LeftField  : "studyId";
                var rightIdField = !string.IsNullOrEmpty(plan.Correlate.RightField) ? plan.Correlate.RightField : leftIdField;

                var completionByKey = rightRows
                    .GroupBy(r => GetStringField(r, rightIdField))
                    .Where(g => g.Key != null)
                    .ToDictionary(g => g.Key!, g => ApplyDateAggregate(g.Cast<object>(), aggregate.Field, aggregate.Fn));

                var allClassified = leftRows.Select(s =>
                {
                    var key = GetStringField(s, leftIdField);
                    completionByKey.TryGetValue(key ?? "", out var actual);
                    return ClassifyRecord(s, actual, leftIdField);
                }).ToList();

                summary = new QuerySummary(
                    allClassified.Count(r => r.Classification == "On Time"),
                    allClassified.Count(r => r.Classification == "Delayed"),
                    allClassified.Count(r => r.Classification == "Indeterminate"));

                rows = allClassified
                    .Where(r => plan.Output.IncludeClassifications.Contains(r.Classification))
                    .Take(plan.Limits.MaxRows)
                    .ToList();
            }
        }

        var servicesCalled = plan.Operations.Select(o => o.Service).Distinct().ToList();
        return new ExecutionOutput(summary, rows, datasets, servicesCalled);
    }

    // ── Generic classification using reflection ───────────────────────────────

    internal static StudyCompletionResult ClassifyRecord(object left, DateTime? actual, string idField)
    {
        var id       = GetStringField(left, idField) ?? GetStringField(left, "id") ?? "";
        var code     = GetStringField(left, "studyCode") ?? GetStringField(left, "code") ?? id;
        var customer = GetStringField(left, "customer") ?? "";
        var planned  = left.GetType().GetProperty("plannedCompletionDate", PropFlags)?.GetValue(left) as DateTime?;

        var flags  = new List<string>();
        string classification, reason;

        if (planned is null)
        {
            classification = "Indeterminate";
            reason = "Planned completion date is missing.";
            flags.Add("missing_planned_completion_date");
        }
        else if (actual is null)
        {
            classification = "Indeterminate";
            reason = "No actual completion timestamp found.";
            flags.Add("no_actual_completion");
        }
        else if (actual > planned)
        {
            classification = "Delayed";
            var days = (actual.Value.Date - planned.Value.Date).Days;
            reason = $"Actual completion is {days} day(s) after planned.";
        }
        else
        {
            classification = "On Time";
            reason = "Actual completion is on or before planned.";
        }

        return new StudyCompletionResult
        {
            StudyId               = id,
            StudyCode             = code,
            Customer              = customer,
            PlannedCompletionDate = planned,
            ActualCompletionDate  = actual,
            Classification        = classification,
            Reason                = reason,
            DataQualityFlags      = flags
        };
    }

    // Returns the first service name (not in 'excluding') whose records have the given field.
    private static string? FindServiceWithField(
        Dictionary<string, List<object>> byService,
        string fieldName,
        string? excluding = null)
    {
        return byService
            .Where(kvp => kvp.Key != excluding && kvp.Value.Count > 0)
            .FirstOrDefault(kvp =>
                kvp.Value[0].GetType().GetProperty(fieldName, PropFlags) != null)
            .Key;
    }

    // ── Filters ───────────────────────────────────────────────────────────────

    private static bool PassesFilters(object record, List<PlanFilter> filters)
    {
        foreach (var f in filters)
        {
            if (!EvaluateFilter(GetStringField(record, f.Field), f.Op, f.Value))
                return false;
        }
        return true;
    }

    private static bool EvaluateFilter(string? fieldVal, string op, string? filterVal) =>
        op.ToLowerInvariant() switch
        {
            "="           => string.Equals(fieldVal, filterVal, StringComparison.OrdinalIgnoreCase),
            "!="          => !string.Equals(fieldVal, filterVal, StringComparison.OrdinalIgnoreCase),
            "is null"     => fieldVal is null or "",
            "is not null" => fieldVal is not null and not "",
            _             => true
        };

    // ── Reflection helpers ────────────────────────────────────────────────────

    private static readonly BindingFlags PropFlags =
        BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance;

    private static string? GetStringField(object obj, string field) =>
        obj.GetType().GetProperty(field, PropFlags)?.GetValue(obj)?.ToString();

    private static Dictionary<string, object?> ToDict(object obj)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in obj.GetType().GetProperties(PropFlags))
        {
            var name = char.ToLowerInvariant(p.Name[0]) + p.Name[1..];
            dict[name] = p.GetValue(obj);
        }
        return dict;
    }

    private static DateTime? ApplyDateAggregate(IEnumerable<object> records, string field, string fn)
    {
        var dates = records
            .Select(r => r.GetType().GetProperty(field, PropFlags)?.GetValue(r))
            .Select(v => v is DateTime dt ? dt : (DateTime?)null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

        if (dates.Count == 0) return null;
        return fn.ToLowerInvariant() switch
        {
            "min" => dates.Min(),
            "max" => dates.Max(),
            _     => dates.Max()
        };
    }
}
