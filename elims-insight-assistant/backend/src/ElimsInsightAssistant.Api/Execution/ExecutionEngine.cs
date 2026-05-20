using System.Reflection;
using System.Runtime.CompilerServices;
using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;

[assembly: InternalsVisibleTo("ElimsInsightAssistant.Tests")]

namespace ElimsInsightAssistant.Api.Execution;

public record ExecutionOutput(
    QuerySummary Summary,
    List<Dictionary<string, object?>> Rows,
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

        // ── Build the Results list. Two modes:
        //    • Classification mode (timeliness query): rows are dicts of the
        //      primaryEntity record fields + classification metadata.
        //    • Plain mode: rows are dicts of the primaryEntity (or first
        //      fetched service) so the UI table reflects whatever the query
        //      actually targets — no hard-coded shape.
        var rows = new List<Dictionary<string, object?>>();
        var summary = new QuerySummary();

        var wantsClassification = plan.Output.IncludeClassifications.Count > 0;
        var classificationProduced = false;
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
                classificationProduced = true;
                var leftRows  = rawByService[leftService];
                var rightRows = rawByService[rightService];

                var leftIdField  = !string.IsNullOrEmpty(plan.Correlate.LeftField)  ? plan.Correlate.LeftField  : "studyId";
                var rightIdField = !string.IsNullOrEmpty(plan.Correlate.RightField) ? plan.Correlate.RightField : leftIdField;

                var completionByKey = rightRows
                    .GroupBy(r => GetStringField(r, rightIdField))
                    .Where(g => g.Key != null)
                    .ToDictionary(g => g.Key!, g => ApplyDateAggregate(g.Cast<object>(), aggregate.Field, aggregate.Fn));

                // Decide which entity the classification is *attributed to*.
                // Default = the side that owns plannedCompletionDate (leftService).
                // If the plan declares a primaryEntity that matches another fetched
                // contract, attribute classification to those rows instead and look
                // up the parent's planned date via the join key.
                var primaryService = !string.IsNullOrWhiteSpace(plan.PrimaryEntity)
                                     && rawByService.ContainsKey(plan.PrimaryEntity)
                    ? plan.PrimaryEntity
                    : leftService;

                List<Dictionary<string, object?>> allClassified;
                if (string.Equals(primaryService, leftService, StringComparison.OrdinalIgnoreCase))
                {
                    allClassified = leftRows.Select(s =>
                    {
                        var key = GetStringField(s, leftIdField);
                        completionByKey.TryGetValue(key ?? "", out var actual);
                        return ClassifyRecord(s, parent: s, actual);
                    }).ToList();
                }
                else
                {
                    var parentByKey = leftRows
                        .GroupBy(r => GetStringField(r, leftIdField))
                        .Where(g => g.Key != null)
                        .ToDictionary(g => g.Key!, g => g.First());

                    var primaryRows = rawByService[primaryService];

                    allClassified = primaryRows.Select(p =>
                    {
                        var parentKey = GetStringField(p, leftIdField);
                        completionByKey.TryGetValue(parentKey ?? "", out var actual);
                        parentByKey.TryGetValue(parentKey ?? "", out var parent);
                        return ClassifyRecord(p, parent, actual);
                    }).ToList();
                }

                summary = new QuerySummary(
                    allClassified.Count(r => Equals(r.GetValueOrDefault("classification"), "On Time")),
                    allClassified.Count(r => Equals(r.GetValueOrDefault("classification"), "Delayed")),
                    allClassified.Count(r => Equals(r.GetValueOrDefault("classification"), "Indeterminate")));

                rows = allClassified
                    .Where(r => plan.Output.IncludeClassifications.Contains(
                        r.GetValueOrDefault("classification")?.ToString() ?? ""))
                    .Take(plan.Limits.MaxRows)
                    .ToList();
            }
        }

        // Fallback: classification was not requested, or was requested but
        // couldn't run (missing join side). Either way, populate Results with
        // the primary entity's rows so the UI grid reflects what was queried.
        // We also honour the correlate as an inner-join: when other services
        // were fetched (and filtered), the primary entity is restricted to
        // rows whose join key appears in EVERY other fetched service's results.
        // That way a planner that filters one side (e.g. study-service by
        // studyCode = ST-006) automatically restricts the primary entity too.
        if (!classificationProduced)
        {
            var primaryService = !string.IsNullOrWhiteSpace(plan.PrimaryEntity)
                                 && rawByService.ContainsKey(plan.PrimaryEntity)
                ? plan.PrimaryEntity
                : rawByService.Keys.FirstOrDefault();

            if (primaryService != null && rawByService.TryGetValue(primaryService, out var primaryRows))
            {
                var joinField = !string.IsNullOrEmpty(plan.Correlate.LeftField)  ? plan.Correlate.LeftField
                               : !string.IsNullOrEmpty(plan.Correlate.RightField) ? plan.Correlate.RightField
                               : "studyId";

                var primaryHasJoinField = primaryRows.Count > 0
                    && primaryRows[0].GetType().GetProperty(joinField, PropFlags) is not null;

                HashSet<string>? allowedKeys = null;
                if (primaryHasJoinField)
                {
                    foreach (var kvp in rawByService)
                    {
                        if (string.Equals(kvp.Key, primaryService, StringComparison.OrdinalIgnoreCase)) continue;
                        if (kvp.Value.Count == 0) continue;
                        if (kvp.Value[0].GetType().GetProperty(joinField, PropFlags) is null) continue;

                        var keys = kvp.Value
                            .Select(o => GetStringField(o, joinField))
                            .Where(s => s is not null)!
                            .Cast<string>();

                        allowedKeys = allowedKeys is null
                            ? new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase)
                            : new HashSet<string>(
                                allowedKeys.Intersect(keys, StringComparer.OrdinalIgnoreCase),
                                StringComparer.OrdinalIgnoreCase);
                    }
                }

                IEnumerable<object> filtered = primaryRows;
                if (allowedKeys is not null)
                {
                    filtered = primaryRows.Where(p =>
                        allowedKeys.Contains(GetStringField(p, joinField) ?? ""));
                }

                rows = filtered.Take(plan.Limits.MaxRows).Select(ToDict).ToList();
            }
        }

        var servicesCalled = plan.Operations.Select(o => o.Service).Distinct().ToList();
        return new ExecutionOutput(summary, rows, datasets, servicesCalled);
    }

    // ── Generic classification using reflection ───────────────────────────────

    // Builds a generic row for a record + its (possibly different) timeliness parent.
    // The dict starts with the record's own fields (preserving declaration order)
    // and appends classification metadata on top. The UI renders columns from the
    // dict keys so nothing in the schema is hard-coded.
    internal static Dictionary<string, object?> ClassifyRecord(object presentation, object? parent, DateTime? actual)
    {
        var source  = parent ?? presentation;
        var planned = source.GetType().GetProperty("plannedCompletionDate", PropFlags)?.GetValue(source) as DateTime?;

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

        var row = ToDict(presentation);
        // When primary differs from parent, surface parent customer/code so the
        // user can still see which study a testp/sample belongs to.
        if (!ReferenceEquals(presentation, parent) && parent is not null)
        {
            foreach (var key in new[] { "studyCode", "customer" })
            {
                if (!row.ContainsKey(key))
                {
                    var val = GetStringField(parent, key);
                    if (val is not null) row[key] = val;
                }
            }
        }
        row["plannedCompletionDate"] = planned;
        row["actualCompletionDate"]  = actual;
        row["classification"]        = classification;
        row["reason"]                = reason;
        row["dataQualityFlags"]      = flags;
        return row;
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
