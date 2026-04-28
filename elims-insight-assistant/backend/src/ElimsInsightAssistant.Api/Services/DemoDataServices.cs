using System.Text.Json;
using ElimsInsightAssistant.Api.Models;

namespace ElimsInsightAssistant.Api.Services;

public interface IStudyServiceClient { Task<List<StudyDto>> ListStudiesAsync(); }
public interface ICoreLabsServiceClient { Task<List<TestPDto>> ListTestPsAsync(); }

public class DemoStudyServiceClient(IWebHostEnvironment env) : IStudyServiceClient
{
    public async Task<List<StudyDto>> ListStudiesAsync()
    {
        var file = Path.Combine(env.ContentRootPath, "..", "..", "..", "..", "seed-data", "studies.json");
        var json = await File.ReadAllTextAsync(Path.GetFullPath(file));
        return JsonSerializer.Deserialize<List<StudyDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }
}

public class DemoCoreLabsServiceClient(IWebHostEnvironment env) : ICoreLabsServiceClient
{
    public async Task<List<TestPDto>> ListTestPsAsync()
    {
        var file = Path.Combine(env.ContentRootPath, "..", "..", "..", "..", "seed-data", "testps.json");
        var json = await File.ReadAllTextAsync(Path.GetFullPath(file));
        return JsonSerializer.Deserialize<List<TestPDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }
}

public interface IClassificationService
{
    StudyCompletionResult Classify(StudyDto study, DateTime? actualCompletionDate);
}

public class StudyCompletionClassificationService : IClassificationService
{
    public StudyCompletionResult Classify(StudyDto study, DateTime? actualCompletionDate)
    {
        var flags = new List<string>();
        var classification = "On Time";
        var reason = "Actual completion date is on or before planned completion date.";

        if (study.PlannedCompletionDate is null)
        {
            classification = "Indeterminate";
            flags.Add("missing_planned_completion_date");
        }

        if (actualCompletionDate is null)
        {
            classification = "Indeterminate";
            flags.Add("no_completed_testps");
        }

        if (classification != "Indeterminate" && actualCompletionDate > study.PlannedCompletionDate)
        {
            classification = "Delayed";
            var days = (actualCompletionDate.Value.Date - study.PlannedCompletionDate!.Value.Date).Days;
            reason = $"Actual completion date is {days} days after planned completion date.";
        }
        else if (classification == "Indeterminate")
        {
            reason = "Planned completion date or completed TestP timestamp is missing.";
        }

        return new StudyCompletionResult
        {
            StudyId = study.StudyId,
            StudyCode = study.StudyCode,
            Customer = study.Customer,
            PlannedCompletionDate = study.PlannedCompletionDate,
            ActualCompletionDate = actualCompletionDate,
            Classification = classification,
            Reason = reason,
            DataQualityFlags = flags
        };
    }
}
