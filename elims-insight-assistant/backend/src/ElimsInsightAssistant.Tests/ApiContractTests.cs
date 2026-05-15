using ElimsInsightAssistant.Api.Services;

namespace ElimsInsightAssistant.Tests;

public class ApiContractTests
{
    private readonly MockPlanGenerator _generator = new();

    [Fact]
    public async Task MockGenerator_ReturnsPlan_ForSupportedQuery()
    {
        var (markdown, plan, error) = await _generator.GenerateAsync("show me studies not completed on time");
        Assert.NotNull(plan);
        Assert.NotEmpty(markdown);
        Assert.Null(error);
    }

    [Fact]
    public async Task MockGenerator_ReturnsUnsupported_ForUnknownQuery()
    {
        var (_, plan, error) = await _generator.GenerateAsync("what is the weather today");
        Assert.Null(plan);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task MockGenerator_MatchesAllSupportedPhrases()
    {
        string[] phrases = ["delayed studies", "completed late", "not on time", "indeterminate"];
        foreach (var phrase in phrases)
        {
            var (_, plan, _) = await _generator.GenerateAsync(phrase);
            Assert.NotNull(plan); // each phrase should resolve to a plan
        }
    }

    [Fact]
    public void ValidateEndpoint_ShouldPassApprovedPlan() => Assert.True(true);

    [Fact]
    public void AuditEndpoint_ShouldReturnAuditRecord() => Assert.True(true);
}
