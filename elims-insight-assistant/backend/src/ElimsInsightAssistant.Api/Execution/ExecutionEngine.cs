using System.Reflection;
using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;

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
    private readonly IClassificationService _classifier;

    public ExecutionEngine(
        IStudyServiceClient studyClient,
        ICoreLabsServiceClient coreLabsClient,
        IProtocolServiceClient protocolClient,
        ISampleServiceClient sampleClient,
        IClassificationService classifier)
    {
        _dataSources = new(StringComparer.OrdinalIgnoreCase)
        {
            ["study-service"]    = async () => (await studyClient.ListStudiesAsync()).Cast<object>(),
            ["corelabs-service"] = async () => (await coreLabsClient.ListTestPsAsync()).Cast<object>(),
            ["protocol-service"] = async () => (await protocolClient.ListProtocolsAsync()).Cast<object>(),
            ["sample-service"]   = async () => (await sampleClient.ListSamplesAsync()).Cast<object>()
        };
        _classifier = classifier;
    }

    public async Task<ExecutionOutput> ExecuteAsync(ExecutionPlan plan, UserContext userContext)
    {
        if (!userContext.Roles.Contains("StudyViewer") || !userContext.Roles.Contains("CoreLabsViewer"))
            throw new UnauthorizedAccessException("User lacks required roles.");

        // ── Fetch & filter every contract referenced in the plan ──────────────
        var rawByService = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in plan.Operations)
        {
            if (!_dataSources.TryGetValue(op.Service, out var fetch)) continue;
            var data = (await fetch()).Where(r => PassesFilters(r, op.Filters)).ToList();
            rawByService[op.Service] = data;
        }

        // ── Apply legal-entity authorization to studies ───────────────────────
        if (rawByService.TryGetValue("study-service", out var studyObjs))
        {
            rawByService["study-service"] = studyObjs
                .Cast<StudyDto>()
                .Where(s => userContext.LegalEntities.Contains(s.LegalEntity))
                .Cast<object>()
                .ToList();
        }

        // ── Project raw data into dictionaries for the response ───────────────
        var datasets = rawByService.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Select(ToDict).ToList());

        // ── If this is a timeliness query (studies + testps both present and
        //    classifications requested), run the classification flow ─────────
        var rows = new List<StudyCompletionResult>();
        var summary = new QuerySummary();

        var wantsClassification = plan.Output.IncludeClassifications.Count > 0;
        if (wantsClassification &&
            rawByService.TryGetValue("study-service", out var sObjs) &&
            rawByService.TryGetValue("corelabs-service", out var tObjs))
        {
            var studies = sObjs.Cast<StudyDto>().ToList();
            var testps  = tObjs.Cast<TestPDto>().ToList();

            var rightField = plan.Correlate?.RightField ?? "studyId";
            var aggregate  = plan.Transform?.Aggregates?.FirstOrDefault()
                             ?? new PlanAggregate("completedAt", "max", "actualCompletionDate");

            var completionByStudy = testps
                .GroupBy(t => GetStringField(t, rightField))
                .Where(g => g.Key != null)
                .ToDictionary(
                    g => g.Key!,
                    g => ApplyDateAggregate(g.Cast<object>(), aggregate.Field, aggregate.Fn));

            var allClassified = studies.Select(s =>
            {
                completionByStudy.TryGetValue(s.StudyId, out var actual);
                return _classifier.Classify(s, actual);
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

        var servicesCalled = plan.Operations.Select(o => o.Service).Distinct().ToList();
        return new ExecutionOutput(summary, rows, datasets, servicesCalled);
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
