using System.Text.Json;
using ElimsInsightAssistant.Api.Models;

namespace ElimsInsightAssistant.Api.Services;

public interface IStudyServiceClient    { Task<List<StudyDto>>    ListStudiesAsync(); }
public interface ICoreLabsServiceClient { Task<List<TestPDto>>    ListTestPsAsync(); }
public interface IProtocolServiceClient { Task<List<ProtocolDto>> ListProtocolsAsync(); }
public interface ISampleServiceClient   { Task<List<SampleDto>>   ListSamplesAsync(); }

internal static class SeedLoader
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static async Task<List<T>> LoadAsync<T>(IWebHostEnvironment env, string filename)
    {
        var file = Path.Combine(env.ContentRootPath, "..", "..", "..", "seed-data", filename);
        var json = await File.ReadAllTextAsync(Path.GetFullPath(file));
        return JsonSerializer.Deserialize<List<T>>(json, Options) ?? [];
    }
}

public class DemoStudyServiceClient(IWebHostEnvironment env) : IStudyServiceClient
{
    public Task<List<StudyDto>> ListStudiesAsync() => SeedLoader.LoadAsync<StudyDto>(env, "studies.json");
}

public class DemoCoreLabsServiceClient(IWebHostEnvironment env) : ICoreLabsServiceClient
{
    public Task<List<TestPDto>> ListTestPsAsync() => SeedLoader.LoadAsync<TestPDto>(env, "testps.json");
}

public class DemoProtocolServiceClient(IWebHostEnvironment env) : IProtocolServiceClient
{
    public Task<List<ProtocolDto>> ListProtocolsAsync() => SeedLoader.LoadAsync<ProtocolDto>(env, "protocols.json");
}

public class DemoSampleServiceClient(IWebHostEnvironment env) : ISampleServiceClient
{
    public Task<List<SampleDto>> ListSamplesAsync() => SeedLoader.LoadAsync<SampleDto>(env, "samples.json");
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
