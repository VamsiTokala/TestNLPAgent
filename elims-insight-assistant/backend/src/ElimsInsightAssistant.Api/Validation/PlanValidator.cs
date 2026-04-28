using ElimsInsightAssistant.Api.Models;

namespace ElimsInsightAssistant.Api.Validation;

public interface IPlanValidator
{
    ValidationResult Validate(ExecutionPlan plan);
}

public class PlanValidator : IPlanValidator
{
    private static readonly Dictionary<string, (HashSet<string> actions, HashSet<string> fields)> Allowlist = new()
    {
        ["study-service"] = ( ["listStudies"], ["studyId", "studyCode", "customer", "legalEntity", "plannedCompletionDate"] ),
        ["corelabs-service"] = ( ["listTestPs"], ["testpId", "studyId", "status", "completedAt", "runType", "result"] )
    };

    private static readonly HashSet<string> AllowedOperators = ["=", "!=", ">", ">=", "<", "<=", "in", "between", "is null", "is not null"];
    private static readonly HashSet<string> AllowedAggs = ["max", "min", "count", "sum", "avg"];
    private static readonly string[] ForbiddenTokens = ["select ", " from ", "drop ", "script", "connectionstring", "update ", "delete ", "insert "];

    public ValidationResult Validate(ExecutionPlan plan)
    {
        var checks = new List<ValidationCheck>();
        var errors = new List<string>();

        foreach (var op in plan.Operations)
        {
            if (!Allowlist.ContainsKey(op.Service)) errors.Add($"Unapproved service: {op.Service}");
            else
            {
                var entry = Allowlist[op.Service];
                if (!entry.actions.Contains(op.Action)) errors.Add($"Unapproved action: {op.Service}.{op.Action}");
                foreach (var field in op.Select.Where(f => !entry.fields.Contains(f))) errors.Add($"Unapproved field: {op.Service}.{field}");
                foreach (var filter in op.Filters)
                {
                    if (!AllowedOperators.Contains(filter.Op.ToLowerInvariant())) errors.Add($"Unapproved operator: {filter.Op}");
                    if (filter.Value is { Length: > 0 } v && ForbiddenTokens.Any(t => v.ToLowerInvariant().Contains(t))) errors.Add("Potential code/SQL fragment detected");
                }
            }
            if (op.Action.Contains("update", StringComparison.OrdinalIgnoreCase) || op.Action.Contains("delete", StringComparison.OrdinalIgnoreCase) || op.Action.Contains("write", StringComparison.OrdinalIgnoreCase))
                errors.Add("Write/update/delete operation is forbidden");
        }

        foreach (var agg in plan.Transform.Aggregates)
            if (!AllowedAggs.Contains(agg.Fn.ToLowerInvariant())) errors.Add($"Unapproved aggregate function: {agg.Fn}");

        if (plan.Limits is null || plan.Limits.MaxRows <= 0) errors.Add("Missing maxRows");
        else if (plan.Limits.MaxRows > 500) errors.Add("maxRows greater than 500");

        checks.Add(new("Service allowlist", errors.Any(e => e.Contains("service")) ? "Failed" : "Passed"));
        checks.Add(new("Field allowlist", errors.Any(e => e.Contains("field")) ? "Failed" : "Passed"));
        checks.Add(new("Read-only execution", errors.Any(e => e.Contains("Write/update/delete")) ? "Failed" : "Passed"));
        checks.Add(new("Aggregation allowlist", errors.Any(e => e.Contains("aggregate")) ? "Failed" : "Passed"));
        checks.Add(new("Result limit", errors.Any(e => e.Contains("maxRows")) ? "Failed" : "Passed"));

        return new ValidationResult(errors.Count == 0 ? "Passed" : "Failed", checks, errors);
    }
}
