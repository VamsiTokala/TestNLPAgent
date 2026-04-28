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

        var studies = (await studyClient.ListStudiesAsync())
            .Where(s => userContext.LegalEntities.Contains(s.LegalEntity))
            .ToList();

        var completedTestPs = (await coreLabsClient.ListTestPsAsync())
            .Where(t => t.Status == "Completed" && t.CompletedAt is not null)
            .ToList();

        var completionByStudy = completedTestPs
            .GroupBy(t => t.StudyId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.CompletedAt));

        var allResults = studies.Select(study =>
        {
            completionByStudy.TryGetValue(study.StudyId, out var actual);
            return classificationService.Classify(study, actual);
        }).ToList();

        var summary = new QuerySummary(
            allResults.Count(r => r.Classification == "On Time"),
            allResults.Count(r => r.Classification == "Delayed"),
            allResults.Count(r => r.Classification == "Indeterminate"));

        var filtered = allResults.Where(r => plan.Output.IncludeClassifications.Contains(r.Classification)).Take(plan.Limits.MaxRows).ToList();
        return (summary, filtered, ["study-service", "corelabs-service"]);
    }
}
