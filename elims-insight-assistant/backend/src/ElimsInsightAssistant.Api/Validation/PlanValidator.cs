using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;

namespace ElimsInsightAssistant.Api.Validation;

public interface IPlanValidator
{
    ValidationResult Validate(ExecutionPlan plan);
}

public class PlanValidator(IServiceRegistry registry) : IPlanValidator
{
    private static readonly HashSet<string> AllowedOperators =
        ["=", "!=", ">", ">=", "<", "<=", "in", "between", "is null", "is not null"];
    private static readonly HashSet<string> AllowedAggs = ["max", "min", "count", "sum", "avg"];
    private static readonly string[] ForbiddenTokens =
        ["select ", " from ", "drop ", "script", "connectionstring", "update ", "delete ", "insert "];

    public ValidationResult Validate(ExecutionPlan plan)
    {
        var contracts = registry.GetAll();
        var allowlist = contracts.ToDictionary(
            c => c.Name,
            c => (actions: new HashSet<string> { c.Action }, fields: new HashSet<string>(c.Fields)));

        var checks = new List<ValidationCheck>();
        var errors = new List<string>();

        // Structural completeness — a partial plan from a confused LLM must not execute.
        // Note: ServiceContractEntry.IsRequired is metadata (UI hint / capability marker),
        // not a per-plan constraint. The planner is free to pick whichever contracts a query needs.
        if (string.IsNullOrWhiteSpace(plan.Intent))
            errors.Add("Intent is required");

        if (plan.Operations is null || plan.Operations.Count == 0)
            errors.Add("Operations cannot be empty");

        checks.Add(new("Plan completeness",
            errors.Any(e => e.Contains("Intent") || e.Contains("Operations"))
                ? "Failed" : "Passed"));

        foreach (var op in plan.Operations ?? [])
        {
            if (string.IsNullOrWhiteSpace(op.Service))
            {
                errors.Add("Operation is missing required 'service' field");
                continue;
            }
            if (!allowlist.ContainsKey(op.Service))
            {
                errors.Add($"Unapproved service: {op.Service}");
            }
            else
            {
                var entry = allowlist[op.Service];
                if (!string.IsNullOrWhiteSpace(op.Action) && !entry.actions.Contains(op.Action))
                    errors.Add($"Unapproved action: {op.Service}.{op.Action}");
                foreach (var field in (op.Select ?? []).Where(f => !entry.fields.Contains(f)))
                    errors.Add($"Unapproved field: {op.Service}.{field}");
                foreach (var filter in op.Filters ?? [])
                {
                    if (string.IsNullOrWhiteSpace(filter.Op) ||
                        !AllowedOperators.Contains(filter.Op.ToLowerInvariant()))
                        errors.Add($"Unapproved operator: {filter.Op}");
                    if (filter.Value is { Length: > 0 } v &&
                        ForbiddenTokens.Any(t => v.ToLowerInvariant().Contains(t)))
                        errors.Add("Potential code/SQL fragment detected");
                }
            }
            if (!string.IsNullOrEmpty(op.Action) &&
                (op.Action.Contains("update", StringComparison.OrdinalIgnoreCase) ||
                 op.Action.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
                 op.Action.Contains("write",  StringComparison.OrdinalIgnoreCase)))
                errors.Add("Write/update/delete operation is forbidden");
        }

        foreach (var agg in plan.Transform?.Aggregates ?? [])
            if (!AllowedAggs.Contains(agg.Fn.ToLowerInvariant()))
                errors.Add($"Unapproved aggregate function: {agg.Fn}");

        if (plan.Limits is null || plan.Limits.MaxRows <= 0) errors.Add("Missing maxRows");
        else if (plan.Limits.MaxRows > 500) errors.Add("maxRows greater than 500");

        checks.Add(new("Service allowlist",    errors.Any(e => e.Contains("Unapproved service"))    ? "Failed" : "Passed"));
        checks.Add(new("Field allowlist",      errors.Any(e => e.Contains("field"))                 ? "Failed" : "Passed"));
        checks.Add(new("Read-only execution",  errors.Any(e => e.Contains("Write/update/delete"))   ? "Failed" : "Passed"));
        checks.Add(new("Aggregation allowlist",errors.Any(e => e.Contains("aggregate"))             ? "Failed" : "Passed"));
        checks.Add(new("Result limit",         errors.Any(e => e.Contains("maxRows"))               ? "Failed" : "Passed"));

        return new ValidationResult(errors.Count == 0 ? "Passed" : "Failed", checks, errors);
    }
}
