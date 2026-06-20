using ElimsInsightAssistant.Api.Execution;
using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;

namespace ElimsInsightAssistant.Tests;

public class ExecutionEngineTests
{
    private sealed class FakeStudyClient(List<StudyDto> data) : IStudyServiceClient
    {
        public Task<List<StudyDto>> ListStudiesAsync() => Task.FromResult(data);
    }

    private sealed class FakeCoreLabsClient(List<TestPDto> data) : ICoreLabsServiceClient
    {
        public Task<List<TestPDto>> ListTestPsAsync() => Task.FromResult(data);
    }

    private sealed class FakeProtocolClient(List<ProtocolDto> data) : IProtocolServiceClient
    {
        public Task<List<ProtocolDto>> ListProtocolsAsync() => Task.FromResult(data);
    }

    private sealed class FakeSampleClient(List<SampleDto> data) : ISampleServiceClient
    {
        public Task<List<SampleDto>> ListSamplesAsync() => Task.FromResult(data);
    }

    private static ExecutionEngine BuildEngine(
        List<StudyDto>? studies = null,
        List<TestPDto>? testps = null,
        List<ProtocolDto>? protocols = null,
        List<SampleDto>? samples = null) =>
        new(new FakeStudyClient(studies ?? []),
            new FakeCoreLabsClient(testps ?? []),
            new FakeProtocolClient(protocols ?? []),
            new FakeSampleClient(samples ?? []));

    private static UserContext User(params string[] legalEntities) =>
        new("u1", ["Analyst"], [.. legalEntities]);

    // ── GroupBy / aggregate (bug #1: distinct-count and group-by queries) ──────

    [Fact]
    public async Task GroupBy_WithNoAggregate_ReturnsOneRowPerDistinctValue()
    {
        var studies = new List<StudyDto>
        {
            new("S1", "ST-001", "Acme",   "DS-BIOANALYTICS", new DateTime(2026, 4, 10)),
            new("S2", "ST-002", "Acme",   "DS-BIOANALYTICS", new DateTime(2026, 4, 11)),
            new("S3", "ST-003", "Globex", "DS-DMPK", new DateTime(2026, 4, 12)),
        };
        var plan = new ExecutionPlan
        {
            Operations = [new PlanOperation("study-service", "listStudies", [], [])],
            Transform = new PlanTransform(GroupBy: ["legalEntity"])
        };

        var engine = BuildEngine(studies: studies);
        var result = await engine.ExecuteAsync(plan, User("DS-BIOANALYTICS", "DS-DMPK"));

        Assert.Equal(2, result.Rows.Count);
        Assert.Contains(result.Rows, r => Equals(r["legalEntity"], "DS-BIOANALYTICS"));
        Assert.Contains(result.Rows, r => Equals(r["legalEntity"], "DS-DMPK"));
    }

    [Fact]
    public async Task GroupBy_WithCountAggregate_CountsRowsPerGroup()
    {
        var samples = new List<SampleDto>
        {
            new("SM1", "S1", "Blood", "Received", new DateTime(2026, 4, 5), "SiteA"),
            new("SM2", "S1", "Blood", "Received", new DateTime(2026, 4, 6), "SiteA"),
            new("SM3", "S2", "Urine", "Received", new DateTime(2026, 4, 7), "SiteB"),
        };
        var plan = new ExecutionPlan
        {
            Operations = [new PlanOperation("sample-service", "listSamples", [], [])],
            Transform = new PlanTransform(GroupBy: ["sampleType"], Aggregates: [new PlanAggregate("*", "count", "count")])
        };

        var engine = BuildEngine(samples: samples);
        var result = await engine.ExecuteAsync(plan, User());

        Assert.Equal(2, result.Rows.Count);
        var blood = result.Rows.Single(r => Equals(r["sampleType"], "Blood"));
        var urine = result.Rows.Single(r => Equals(r["sampleType"], "Urine"));
        Assert.Equal(2, blood["count"]);
        Assert.Equal(1, urine["count"]);
    }

    // ── Between filter (bug #2: must fail closed with a single bound) ──────────

    [Fact]
    public async Task Between_WithSingleBound_MatchesNothing()
    {
        var studies = new List<StudyDto>
        {
            new("S1", "ST-001", "Acme", "DS-BIOANALYTICS", new DateTime(2026, 4, 10)),
            new("S2", "ST-002", "Acme", "DS-BIOANALYTICS", new DateTime(2026, 4, 20)),
        };
        var plan = new ExecutionPlan
        {
            Operations =
            [
                new PlanOperation("study-service", "listStudies", [],
                    [new PlanFilter("plannedCompletionDate", "between", "[\"2026-04-01\"]")])
            ]
        };

        var engine = BuildEngine(studies: studies);
        var result = await engine.ExecuteAsync(plan, User("DS-BIOANALYTICS"));

        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task Between_WithTwoBounds_MatchesWithinRange()
    {
        var studies = new List<StudyDto>
        {
            new("S1", "ST-001", "Acme", "DS-BIOANALYTICS", new DateTime(2026, 4, 10)),
            new("S2", "ST-002", "Acme", "DS-BIOANALYTICS", new DateTime(2026, 5, 20)),
        };
        var plan = new ExecutionPlan
        {
            Operations =
            [
                new PlanOperation("study-service", "listStudies", [],
                    [new PlanFilter("plannedCompletionDate", "between", "[\"2026-04-01\",\"2026-04-30\"]")])
            ]
        };

        var engine = BuildEngine(studies: studies);
        var result = await engine.ExecuteAsync(plan, User("DS-BIOANALYTICS"));

        Assert.Single(result.Rows);
        Assert.Equal("S1", result.Rows[0]["studyId"]);
    }

    // ── Sort ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sort_Descending_OrdersRowsByFieldDescending()
    {
        var studies = new List<StudyDto>
        {
            new("S1", "ST-001", "Acme", "DS-BIOANALYTICS", new DateTime(2026, 4, 10)),
            new("S2", "ST-002", "Acme", "DS-BIOANALYTICS", new DateTime(2026, 4, 20)),
            new("S3", "ST-003", "Acme", "DS-BIOANALYTICS", new DateTime(2026, 4, 15)),
        };
        var plan = new ExecutionPlan
        {
            Operations = [new PlanOperation("study-service", "listStudies", [], [])],
            Sort = [new PlanSort("plannedCompletionDate", "desc")]
        };

        var engine = BuildEngine(studies: studies);
        var result = await engine.ExecuteAsync(plan, User("DS-BIOANALYTICS"));

        Assert.Equal(["S2", "S3", "S1"], result.Rows.Select(r => r["studyId"]));
    }

    // ── Pagination ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Pagination_RoundTripsAcrossPagesViaNextPageToken()
    {
        var studies = Enumerable.Range(1, 5)
            .Select(i => new StudyDto($"S{i}", $"ST-00{i}", "Acme", "DS-BIOANALYTICS", new DateTime(2026, 4, i + 1)))
            .ToList();
        var plan = new ExecutionPlan
        {
            Operations = [new PlanOperation("study-service", "listStudies", [], [])],
            Sort = [new PlanSort("studyId", "asc")],
            Limits = new PlanLimits(2, true)
        };

        var engine = BuildEngine(studies: studies);

        var page1 = await engine.ExecuteAsync(plan, User("DS-BIOANALYTICS"), offset: 0);
        Assert.Equal(["S1", "S2"], page1.Rows.Select(r => r["studyId"]));
        Assert.Equal("2", page1.NextPageToken);

        var page2 = await engine.ExecuteAsync(plan, User("DS-BIOANALYTICS"), offset: int.Parse(page1.NextPageToken!));
        Assert.Equal(["S3", "S4"], page2.Rows.Select(r => r["studyId"]));
        Assert.Equal("4", page2.NextPageToken);

        var page3 = await engine.ExecuteAsync(plan, User("DS-BIOANALYTICS"), offset: int.Parse(page2.NextPageToken!));
        Assert.Equal(["S5"], page3.Rows.Select(r => r["studyId"]));
        Assert.Null(page3.NextPageToken);
    }

    // ── Projection ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Projection_LimitsRowsToSelectedFields()
    {
        var studies = new List<StudyDto> { new("S1", "ST-001", "Acme", "DS-BIOANALYTICS", new DateTime(2026, 4, 10)) };
        var plan = new ExecutionPlan
        {
            Operations = [new PlanOperation("study-service", "listStudies", ["studyId", "customer"], [])]
        };

        var engine = BuildEngine(studies: studies);
        var result = await engine.ExecuteAsync(plan, User("DS-BIOANALYTICS"));

        Assert.Single(result.Rows);
        Assert.Equal(new HashSet<string> { "studyId", "customer" }, result.Rows[0].Keys.ToHashSet());
    }
}
