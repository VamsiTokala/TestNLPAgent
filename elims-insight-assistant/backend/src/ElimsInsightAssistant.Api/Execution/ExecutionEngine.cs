using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;

[assembly: InternalsVisibleTo("ElimsInsightAssistant.Tests")]

namespace ElimsInsightAssistant.Api.Execution;

public record ExecutionOutput(
    QuerySummary Summary,
    List<Dictionary<string, object?>> Rows,
    Dictionary<string, List<Dictionary<string, object?>>> Datasets,
    List<string> ServicesCalled,
    string? NextPageToken = null);

public interface IExecutionEngine
{
    Task<ExecutionOutput> ExecuteAsync(ExecutionPlan plan, UserContext userContext, int offset = 0);
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

    public async Task<ExecutionOutput> ExecuteAsync(ExecutionPlan plan, UserContext userContext, int offset = 0)
    {
        if (userContext.Roles.Count == 0)
            throw new UnauthorizedAccessException("User has no assigned roles.");

        var correlate = plan.Correlate ?? new PlanCorrelate();

        // Fields the plan actually asked for, per service — drives projection so
        // the response carries only the columns the query needs, not full DTOs.
        var selectByService = plan.Operations
            .Where(op => !string.IsNullOrWhiteSpace(op.Service) && op.Select is { Count: > 0 })
            .GroupBy(op => op.Service, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlySet<string>)g.SelectMany(op => op.Select).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

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
            kvp => kvp.Value.Select(o => ToDict(o, selectByService.GetValueOrDefault(kvp.Key))).ToList());

        string? nextPageToken = null;

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

                var leftIdField  = !string.IsNullOrEmpty(correlate.LeftField)  ? correlate.LeftField  : "studyId";
                var rightIdField = !string.IsNullOrEmpty(correlate.RightField) ? correlate.RightField : leftIdField;

                var completionByKey = rightRows
                    .GroupBy(r => GetStringField(r, rightIdField))
                    .Where(g => g.Key != null)
                    .ToDictionary(g => g.Key!, g => ApplyDateAggregate(g.Cast<object>(), aggregate.Field, aggregate.Fn));

                // Same grouping, but keeping the raw sibling records (not just the
                // aggregated date) so ClassifyRecord can explain *why* a study has
                // no actual completion yet — e.g. pending vs. failed TestPs —
                // instead of only reporting the date comparison.
                var recordsByKey = rightRows
                    .GroupBy(r => GetStringField(r, rightIdField))
                    .Where(g => g.Key != null)
                    .ToDictionary(g => g.Key!, g => (IEnumerable<object>)g.Cast<object>().ToList());

                // Decide which entity the classification is *attributed to*.
                // Default = the side that owns plannedCompletionDate (leftService).
                // If the plan declares a primaryEntity that matches another fetched
                // contract, attribute classification to those rows instead and look
                // up the parent's planned date via the join key.
                var primaryService = !string.IsNullOrWhiteSpace(plan.PrimaryEntity)
                                     && rawByService.ContainsKey(plan.PrimaryEntity)
                    ? plan.PrimaryEntity
                    : leftService;
                var allowedFields = selectByService.GetValueOrDefault(primaryService);

                List<Dictionary<string, object?>> allClassified;
                if (string.Equals(primaryService, leftService, StringComparison.OrdinalIgnoreCase))
                {
                    allClassified = leftRows.Select(s =>
                    {
                        var key = GetStringField(s, leftIdField);
                        completionByKey.TryGetValue(key ?? "", out var actual);
                        var siblings = recordsByKey.GetValueOrDefault(key ?? "");
                        return ClassifyRecord(s, parent: s, actual, siblings, allowedFields);
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
                        var siblings = recordsByKey.GetValueOrDefault(parentKey ?? "");
                        return ClassifyRecord(p, parent, actual, siblings, allowedFields);
                    }).ToList();
                }

                summary = new QuerySummary(
                    allClassified.Count(r => Equals(r.GetValueOrDefault("classification"), "On Time")),
                    allClassified.Count(r => Equals(r.GetValueOrDefault("classification"), "Delayed")),
                    allClassified.Count(r => Equals(r.GetValueOrDefault("classification"), "Indeterminate")));

                var matching = allClassified
                    .Where(r => plan.Output.IncludeClassifications.Contains(
                        r.GetValueOrDefault("classification")?.ToString() ?? ""))
                    .ToList();
                ApplySort(matching, plan.Sort);
                (rows, nextPageToken) = Paginate(matching, offset, plan.Limits.MaxRows);
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
                var joinField = !string.IsNullOrEmpty(correlate.LeftField)  ? correlate.LeftField
                               : !string.IsNullOrEmpty(correlate.RightField) ? correlate.RightField
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

                // groupBy turns "total / count / breakdown by X" queries into one row
                // per distinct value of X instead of dumping every raw record — e.g.
                // groupBy ["legalEntity"] answers "how many legal entities" via the
                // resulting row count, and groupBy ["sampleType"] answers "samples by
                // type" directly, without pulling the full table into the UI.
                if (plan.Transform.GroupBy.Count > 0)
                {
                    var grouped = BuildGroupedRows(filtered, plan.Transform.GroupBy, plan.Transform.Aggregates);
                    ApplySort(grouped, plan.Sort);
                    (rows, nextPageToken) = Paginate(grouped, offset, plan.Limits.MaxRows);
                }
                else
                {
                    var allowedFields = selectByService.GetValueOrDefault(primaryService);
                    var projected = filtered.Select(o => ToDict(o, allowedFields)).ToList();
                    ApplySort(projected, plan.Sort);
                    (rows, nextPageToken) = Paginate(projected, offset, plan.Limits.MaxRows);
                }
            }
        }

        var servicesCalled = plan.Operations.Select(o => o.Service).Distinct().ToList();
        return new ExecutionOutput(summary, rows, datasets, servicesCalled, nextPageToken);
    }

    // ── Generic classification using reflection ───────────────────────────────

    // Builds a generic row for a record + its (possibly different) timeliness parent.
    // The dict starts with the record's own fields (preserving declaration order)
    // and appends classification metadata on top. The UI renders columns from the
    // dict keys so nothing in the schema is hard-coded.
    internal static Dictionary<string, object?> ClassifyRecord(
        object presentation,
        object? parent,
        DateTime? actual,
        IEnumerable<object>? childRecords = null,
        IReadOnlySet<string>? allowedFields = null)
    {
        var source  = parent ?? presentation;
        var planned = source.GetType().GetProperty("plannedCompletionDate", PropFlags)?.GetValue(source) as DateTime?;
        var siblings = childRecords?.ToList() ?? [];

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
            // Distinguish *why* there's no actual completion yet — pending TestPs,
            // failed TestPs, or no TestP records at all — instead of one generic
            // "no data" reason, so timeliness queries can answer "why is this
            // delayed" rather than just "is this delayed".
            var pending = siblings.Count(r => string.Equals(GetStringField(r, "status"), "Pending", StringComparison.OrdinalIgnoreCase));
            var failed  = siblings.Count(r => string.Equals(GetStringField(r, "result"), "Fail", StringComparison.OrdinalIgnoreCase));

            if (siblings.Count == 0)
            {
                reason = "No actual completion timestamp found: no TestP records exist for this study.";
                flags.Add("no_testp_records");
            }
            else
            {
                var causes = new List<string>();
                if (pending > 0) { causes.Add($"{pending} TestP(s) pending"); flags.Add("pending_testp"); }
                if (failed  > 0) { causes.Add($"{failed} TestP(s) failed");  flags.Add("failed_testp");  }

                reason = causes.Count > 0
                    ? $"No actual completion timestamp found: {string.Join(", ", causes)}."
                    : "No actual completion timestamp found despite existing TestP records.";
                if (causes.Count == 0) flags.Add("no_actual_completion");
            }
        }
        else if (actual > planned)
        {
            classification = "Delayed";
            var days = (actual.Value.Date - planned.Value.Date).Days;
            reason = $"Actual completion is {days} day(s) after planned.";

            // The study already has an actual completion date (that's how it was
            // classified Delayed), but still-pending or failed sibling TestPs are
            // useful context for *why* it ran late or isn't fully closed out.
            var stillPending = siblings.Count(r => string.Equals(GetStringField(r, "status"), "Pending", StringComparison.OrdinalIgnoreCase));
            var failed       = siblings.Count(r => string.Equals(GetStringField(r, "result"), "Fail", StringComparison.OrdinalIgnoreCase));

            if (failed > 0) { reason += $" {failed} TestP(s) failed."; flags.Add("failed_testp"); }
            if (stillPending > 0) { reason += $" {stillPending} TestP(s) still pending."; flags.Add("pending_testp"); }
        }
        else
        {
            classification = "On Time";
            reason = "Actual completion is on or before planned.";
        }

        var row = ToDict(presentation, allowedFields);
        // When primary differs from parent, surface parent customer/code so the
        // user can still see which study a testp/sample belongs to.
        if (!ReferenceEquals(presentation, parent) && parent is not null)
        {
            foreach (var key in new[] { "studyCode", "customer" })
            {
                if (!row.ContainsKey(key) && (allowedFields is null || allowedFields.Count == 0 || allowedFields.Contains(key)))
                {
                    var val = GetStringField(parent, key);
                    if (val is not null) row[key] = val;
                }
            }
        }
        // Classification fields are the literal answer to a timeliness query, so
        // they're always included regardless of the plan's column projection.
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
            ">"           => CompareValues(fieldVal, filterVal) > 0,
            ">="          => CompareValues(fieldVal, filterVal) >= 0,
            "<"           => CompareValues(fieldVal, filterVal) < 0,
            "<="          => CompareValues(fieldVal, filterVal) <= 0,
            "in"          => ParseFilterValues(filterVal).Any(v => string.Equals(fieldVal, v, StringComparison.OrdinalIgnoreCase)),
            "between"     => MatchesBetween(fieldVal, filterVal),
            "is null"     => fieldVal is null or "",
            "is not null" => fieldVal is not null and not "",
            _             => true
        };

    private static bool MatchesBetween(string? fieldVal, string? filterVal)
    {
        if (string.IsNullOrWhiteSpace(fieldVal)) return false;
        var values = ParseFilterValues(filterVal).ToList();
        // Fail closed: a "between" filter needs two bounds. A single value is an
        // incomplete plan, not "no constraint" — matching everything would
        // silently return unfiltered data instead of surfacing the bad plan.
        if (values.Count < 2) return false;
        return CompareValues(fieldVal, values[0]) >= 0 && CompareValues(fieldVal, values[1]) <= 0;
    }

    private static IEnumerable<string> ParseFilterValues(string? filterVal)
    {
        if (string.IsNullOrWhiteSpace(filterVal))
            return [];

        if (!LooksLikeJson(filterVal))
            return [filterVal];

        try
        {
            using var doc = JsonDocument.Parse(filterVal);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                return [..
                    root.EnumerateArray()
                        .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.GetRawText())];
            }

            return [root.ValueKind == JsonValueKind.String ? root.GetString() ?? string.Empty : root.GetRawText()];
        }
        catch (JsonException)
        {
            return [filterVal];
        }
    }

    private static int CompareValues(string? fieldVal, string? filterVal)
    {
        if (string.IsNullOrWhiteSpace(fieldVal) && string.IsNullOrWhiteSpace(filterVal)) return 0;
        if (string.IsNullOrWhiteSpace(fieldVal)) return -1;
        if (string.IsNullOrWhiteSpace(filterVal)) return 1;

        var normalizedFilter = ParseFilterValues(filterVal).FirstOrDefault() ?? filterVal;

        if (DateTime.TryParse(fieldVal, out var fieldDate) && DateTime.TryParse(normalizedFilter, out var filterDate))
            return fieldDate.CompareTo(filterDate);

        if (decimal.TryParse(fieldVal, out var fieldNumber) && decimal.TryParse(normalizedFilter, out var filterNumber))
            return fieldNumber.CompareTo(filterNumber);

        return string.Compare(fieldVal, normalizedFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("[") || trimmed.StartsWith("{") || trimmed.StartsWith("\"");
    }

    // ── Reflection helpers ────────────────────────────────────────────────────

    private static readonly BindingFlags PropFlags =
        BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance;

    private static string? GetStringField(object obj, string field) =>
        obj.GetType().GetProperty(field, PropFlags)?.GetValue(obj)?.ToString();

    // Projects an object to only the fields the plan selected for its service —
    // a null/empty allow-set means "no projection requested", so every property
    // is included (used internally, e.g. by tests and dataset transparency views).
    private static Dictionary<string, object?> ToDict(object obj, IReadOnlySet<string>? allow = null)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in obj.GetType().GetProperties(PropFlags))
        {
            var name = char.ToLowerInvariant(p.Name[0]) + p.Name[1..];
            if (allow is { Count: > 0 } && !allow.Contains(name)) continue;
            dict[name] = p.GetValue(obj);
        }
        return dict;
    }

    // ── Grouping / aggregation (non-classification "count/total/breakdown by X") ─

    private static List<Dictionary<string, object?>> BuildGroupedRows(
        IEnumerable<object> rows, List<string> groupBy, List<PlanAggregate> aggregates)
    {
        List<PlanAggregate> effectiveAggregates = aggregates.Count > 0
            ? aggregates
            : [new PlanAggregate(groupBy[0], "count", "count")];

        return rows
            .GroupBy(r => string.Join("", groupBy.Select(f => GetStringField(r, f) ?? "")))
            .Select(g =>
            {
                var dict = new Dictionary<string, object?>();
                var first = g.First();
                foreach (var field in groupBy)
                    dict[field] = GetStringField(first, field);
                foreach (var agg in effectiveAggregates)
                    dict[agg.As] = ApplyAggregate(g.Cast<object>(), agg.Field, agg.Fn);
                return dict;
            })
            .ToList();
    }

    private static object? ApplyAggregate(IEnumerable<object> records, string field, string fn)
    {
        var list = records.ToList();
        if (string.Equals(fn, "count", StringComparison.OrdinalIgnoreCase))
            return list.Count;

        var raw = list.Select(r => r.GetType().GetProperty(field, PropFlags)?.GetValue(r)).ToList();

        if (raw.Any(v => v is DateTime))
        {
            var dates = raw.OfType<DateTime>().ToList();
            return fn.ToLowerInvariant() switch
            {
                "min" => dates.Count > 0 ? dates.Min() : null,
                "max" => dates.Count > 0 ? dates.Max() : null,
                _     => dates.Count > 0 ? dates.Max() : (DateTime?)null
            };
        }

        var numbers = raw
            .Select(v => decimal.TryParse(v?.ToString(), out var n) ? n : (decimal?)null)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .ToList();

        return fn.ToLowerInvariant() switch
        {
            "sum" => numbers.Sum(),
            "avg" => numbers.Count > 0 ? numbers.Average() : null,
            "min" => numbers.Count > 0 ? numbers.Min() : null,
            "max" => numbers.Count > 0 ? numbers.Max() : null,
            _     => numbers.Count
        };
    }

    // ── Sorting / pagination — applied uniformly after the answer rows are built ─

    private static void ApplySort(List<Dictionary<string, object?>> rows, List<PlanSort> sort)
    {
        if (sort.Count == 0 || rows.Count == 0) return;
        rows.Sort((a, b) =>
        {
            foreach (var s in sort)
            {
                var cmp = CompareValues(a.GetValueOrDefault(s.Field)?.ToString(), b.GetValueOrDefault(s.Field)?.ToString());
                if (cmp != 0) return string.Equals(s.Direction, "desc", StringComparison.OrdinalIgnoreCase) ? -cmp : cmp;
            }
            return 0;
        });
    }

    // Offset-based continuation token: simple and transparent for in-memory data
    // sources today; the token shape can change to an opaque cursor later without
    // affecting callers, since they only ever round-trip whatever string we hand back.
    private static (List<Dictionary<string, object?>> Page, string? NextToken) Paginate(
        List<Dictionary<string, object?>> all, int offset, int maxRows)
    {
        var safeOffset = Math.Max(0, offset);
        var page = all.Skip(safeOffset).Take(maxRows).ToList();
        var hasMore = safeOffset + page.Count < all.Count;
        return (page, hasMore ? (safeOffset + page.Count).ToString() : null);
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
