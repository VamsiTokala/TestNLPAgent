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

