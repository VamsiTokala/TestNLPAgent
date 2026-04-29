using ElimsInsightAssistant.Api.Models;

namespace ElimsInsightAssistant.Api.Services;

public interface IPlanGenerator
{
    (string markdown, ExecutionPlan? plan, string? error) Generate(string query);
}

public class MockPlanGenerator : IPlanGenerator
{
    private static readonly string[] SupportedTerms = ["not completed on time", "delayed studies", "completed late", "not on time", "indeterminate"];

    public (string markdown, ExecutionPlan? plan, string? error) Generate(string query)
    {
        var q = query.ToLowerInvariant();
        if (!SupportedTerms.Any(q.Contains))
        {
            return (string.Empty, null, "This demo currently supports queries related to study completion timeliness.");
        }

        var markdown = """
# Analysis Plan
Intent: Find studies not completed on time.

## Contract mapping
- Study Service: study identity, customer, legal entity, and planned completion date.
- CoreLabs Service: TestP status and completion timestamps.

## Steps
1. Fetch studies from Study Service.
2. Fetch completed TestPs from CoreLabs Service.
3. Correlate Study and TestP records using studyId.
4. Group TestPs by studyId.
5. Derive actual study completion as the maximum TestP completedAt timestamp.
6. Compare actual completion date with planned completion date.
7. Classify each study as On Time, Delayed, or Indeterminate.
8. Return Delayed and Indeterminate studies with supporting details.

## Execution mode
Read-only, deterministic, approved service contracts only.
""";

        var plan = new ExecutionPlan
        {
            Intent = "find_studies_not_completed_on_time",
            Entities = ["study", "testp"],
            Operations =
            [
                new("study-service", "listStudies", ["studyId", "studyCode", "customer", "legalEntity", "plannedCompletionDate"], []),
                new("corelabs-service", "listTestPs", ["testpId", "studyId", "status", "completedAt", "runType", "result"], [new("status", "=", "Completed")])
            ]
        };

        return (markdown, plan, null);
    }
}
