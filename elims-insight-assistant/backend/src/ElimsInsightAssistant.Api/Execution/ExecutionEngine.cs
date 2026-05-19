using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;

namespace ElimsInsightAssistant.Api.Execution;

public interface IExecutionEngine
{
    Task<(QuerySummary summary, List<StudyCompletionResult> rows, List<string> servicesCalled)> ExecuteAsync(ExecutionPlan plan, UserContext userContext);
}

public class ExecutionEngine(IStudyServiceClient studyClient, ICoreLabsServiceClient coreLabsClient, IClassificationService classificationService) : IExecutionEngine
{
    public async Task<(QuerySummary summary, List<StudyCompletionResult> rows, List<string> servicesCalled)> ExecuteAsync(ExecutionPlan plan, UserContext userContext)
    {
        if (!userContext.Roles.Contains("StudyViewer") || !userContext.Roles.Contains("CoreLabsViewer"))
            throw new UnauthorizedAccessException("User lacks required roles.");

        // ── Fetch studies — apply filters from the plan operation ─────────────
        var studyOp = plan.Operations.FirstOrDefault(o => o.Service == "study-service");
        var studies = (await studyClient.ListStudiesAsync())
            .Where(s => userContext.LegalEntities.Contains(s.LegalEntity))
            .Where(s => PassesFilters(s, studyOp?.Filters ?? []))
            .ToList();

        // ── Fetch TestPs — apply filters from the plan operation ──────────────
        var testpOp = plan.Operations.FirstOrDefault(o => o.Service == "corelabs-service");
        var testPs = (await coreLabsClient.ListTestPsAsync())
            .Where(t => PassesFilters(t, testpOp?.Filters ?? []))
            .ToList();

        // ── Correlate using fields from plan.Correlate ────────────────────────
        var rightField = plan.Correlate.RightField;   // e.g. "studyId"

        // ── Aggregate using plan.Transform.Aggregates ─────────────────────────
        // Each aggregate: { field, fn, as } — derive a DateTime? per group key
        var aggregate = plan.Transform.Aggregates.FirstOrDefault()
                        ?? new PlanAggregate("completedAt", "max", "actualCompletionDate");

        var completionByStudy = testPs
            .GroupBy(t => GetStringField(t, rightField))
            .ToDictionary(
                g => g.Key,
                g => ApplyDateAggregate(g, aggregate.Field, aggregate.Fn));

        // ── Classify each study ───────────────────────────────────────────────
        var allResults = studies.Select(study =>
        {
            completionByStudy.TryGetValue(study.StudyId, out var actual);
            return classificationService.Classify(study, actual);
        }).ToList();

        var summary = new QuerySummary(
            allResults.Count(r => r.Classification == "On Time"),
            allResults.Count(r => r.Classification == "Delayed"),
            allResults.Count(r => r.Classification == "Indeterminate"));

        var filtered = allResults
            .Where(r => plan.Output.IncludeClassifications.Contains(r.Classification))
            .Take(plan.Limits.MaxRows)
            .ToList();

        var servicesCalled = plan.Operations.Select(o => o.Service).ToList();
        return (summary, filtered, servicesCalled);
    }

    // ── Filter evaluation ─────────────────────────────────────────────────────

    private static bool PassesFilters(object record, List<PlanFilter> filters)
    {
        foreach (var f in filters)
        {
            var fieldVal = GetStringField(record, f.Field);
            if (!EvaluateFilter(fieldVal, f.Op, f.Value))
                return false;
        }
        return true;
    }

    private static bool EvaluateFilter(string? fieldVal, string op, string? filterVal) =>
        op.ToLowerInvariant() switch
        {
            "="  => string.Equals(fieldVal, filterVal, StringComparison.OrdinalIgnoreCase),
            "!=" => !string.Equals(fieldVal, filterVal, StringComparison.OrdinalIgnoreCase),
            "is null"     => fieldVal is null or "",
            "is not null" => fieldVal is not null and not "",
            _ => true   // unsupported ops pass through — validator already checked them
        };

    // ── Field accessors via reflection ────────────────────────────────────────

    private static string? GetStringField(object obj, string field)
    {
        var prop = obj.GetType().GetProperty(
            field, System.Reflection.BindingFlags.IgnoreCase |
                   System.Reflection.BindingFlags.Public |
                   System.Reflection.BindingFlags.Instance);
        return prop?.GetValue(obj)?.ToString();
    }

    // ── Date aggregation ──────────────────────────────────────────────────────

    private static DateTime? ApplyDateAggregate(IEnumerable<object> records, string field, string fn)
    {
        var dates = records
            .Select(r =>
            {
                var prop = r.GetType().GetProperty(
                    field, System.Reflection.BindingFlags.IgnoreCase |
                           System.Reflection.BindingFlags.Public |
                           System.Reflection.BindingFlags.Instance);
                var val = prop?.GetValue(r);
                return val is DateTime dt ? dt : (DateTime?)null;
            })
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
