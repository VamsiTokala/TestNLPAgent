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
        var seedDataDir = ResolveSeedDataDirectory(env.ContentRootPath)
            ?? throw new DirectoryNotFoundException(
                $"Could not locate seed-data directory from content root '{env.ContentRootPath}'.");

        var file = Path.Combine(seedDataDir, filename);
        var json = await File.ReadAllTextAsync(file);
        return JsonSerializer.Deserialize<List<T>>(json, Options) ?? [];
    }

    private static string? ResolveSeedDataDirectory(string contentRootPath)
    {
        foreach (var start in CandidateRoots(contentRootPath))
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "seed-data");
                if (Directory.Exists(candidate))
                    return candidate;

                dir = dir.Parent;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateRoots(string contentRootPath)
    {
        yield return contentRootPath;

        var baseDir = AppContext.BaseDirectory;
        if (!string.Equals(baseDir, contentRootPath, StringComparison.OrdinalIgnoreCase))
            yield return baseDir;
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

