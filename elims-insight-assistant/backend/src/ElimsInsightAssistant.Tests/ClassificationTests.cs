using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;

namespace ElimsInsightAssistant.Tests;

public class ClassificationTests
{
    private readonly StudyCompletionClassificationService _svc = new();

    [Fact]
    public void OnTime_WhenActualBeforePlanned()
    {
        var result = _svc.Classify(new StudyDto("S1","ST-001","C","EU",new DateTime(2026,4,10)), new DateTime(2026,4,9));
        Assert.Equal("On Time", result.Classification);
    }

    [Fact]
    public void OnTime_WhenEqual() => Assert.Equal("On Time", _svc.Classify(new("S1","ST","C","EU",new DateTime(2026,4,10)), new DateTime(2026,4,10)).Classification);

    [Fact]
    public void Delayed_WhenAfter() => Assert.Equal("Delayed", _svc.Classify(new("S1","ST","C","EU",new DateTime(2026,4,10)), new DateTime(2026,4,12)).Classification);

    [Fact]
    public void Indeterminate_WhenPlannedMissing() => Assert.Equal("Indeterminate", _svc.Classify(new("S1","ST","C","EU",null), new DateTime(2026,4,12)).Classification);

    [Fact]
    public void Indeterminate_WhenActualMissing() => Assert.Equal("Indeterminate", _svc.Classify(new("S1","ST","C","EU",new DateTime(2026,4,10)), null).Classification);

    [Fact]
    public void UsesMaxTimestampAcrossCompletedTestPs()
    {
        var testps = new[] { new DateTime(2026,4,16), new DateTime(2026,4,17), new DateTime(2026,4,15)};
        var actual = testps.Max();
        var result = _svc.Classify(new("S2","ST-002","C","EU",new DateTime(2026,4,15)), actual);
        Assert.Equal(new DateTime(2026,4,17), result.ActualCompletionDate);
    }
}
