using ElimsInsightAssistant.Api.Services;

namespace ElimsInsightAssistant.Tests;

public class ApiContractTests
{
    private readonly MockPlanGenerator _generator = new(new InMemoryServiceRegistry());

    [Fact]
    public async Task MockGenerator_ReturnsPlan_ForSupportedQuery()
    {
        var result = await _generator.GenerateAsync("show me studies not completed on time");
        Assert.NotNull(result.Plan);
        Assert.NotEmpty(result.Markdown);
        Assert.Null(result.Error);
        Assert.False(result.IsServerError);
    }

    [Fact]
    public async Task MockGenerator_ReturnsUnsupported_ForUnknownQuery()
    {
        var result = await _generator.GenerateAsync("what is the weather today");
        Assert.Null(result.Plan);
        Assert.NotNull(result.Error);
        Assert.False(result.IsServerError); // unsupported ≠ server error
    }

    [Fact]
    public async Task MockGenerator_MatchesAllSupportedPhrases()
    {
        string[] phrases = ["delayed studies", "completed late", "not on time", "indeterminate"];
        foreach (var phrase in phrases)
        {
            var result = await _generator.GenerateAsync(phrase);
            Assert.NotNull(result.Plan);
            Assert.False(result.IsServerError);
        }
    }

    [Fact]
    public void PlanGeneratorResult_IsServerError_DefaultsFalse()
    {
        // Verify the default value — IsServerError must be opt-in, not the default
        var result = new PlanGeneratorResult("md", null, "unsupported");
        Assert.False(result.IsServerError);
    }

    [Fact]
    public void PlanGeneratorResult_IsServerError_CanBeSetTrue()
    {
        // Transient failures must explicitly set IsServerError so the controller returns 503
        var result = new PlanGeneratorResult(string.Empty, null,
            "Plan generation service is temporarily unavailable.", IsServerError: true);
        Assert.True(result.IsServerError);
        Assert.Null(result.Plan);
    }
}
