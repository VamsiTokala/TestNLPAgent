using ElimsInsightAssistant.Api.Execution;
using ElimsInsightAssistant.Api.Models;

namespace ElimsInsightAssistant.Tests;

// Classification logic lives in ExecutionEngine.ClassifyRecord (internal static).
// It accepts any object with plannedCompletionDate + optional studyId/studyCode/customer fields.
// We pass StudyDto directly since it has all those properties.

public class ClassificationTests
{
    private static StudyDto Study(DateTime? planned) =>
        new("S1", "ST-001", "Acme", "EU", planned);

    [Fact]
    public void OnTime_WhenActualBeforePlanned()
    {
        var result = ExecutionEngine.ClassifyRecord(Study(new DateTime(2026, 4, 10)), new DateTime(2026, 4, 9), "studyId");
        Assert.Equal("On Time", result.Classification);
    }

    [Fact]
    public void OnTime_WhenActualEqualsPlanned()
    {
        var result = ExecutionEngine.ClassifyRecord(Study(new DateTime(2026, 4, 10)), new DateTime(2026, 4, 10), "studyId");
        Assert.Equal("On Time", result.Classification);
    }

    [Fact]
    public void Delayed_WhenActualAfterPlanned()
    {
        var result = ExecutionEngine.ClassifyRecord(Study(new DateTime(2026, 4, 10)), new DateTime(2026, 4, 12), "studyId");
        Assert.Equal("Delayed", result.Classification);
    }

    [Fact]
    public void Indeterminate_WhenPlannedMissing()
    {
        var result = ExecutionEngine.ClassifyRecord(Study(null), new DateTime(2026, 4, 12), "studyId");
        Assert.Equal("Indeterminate", result.Classification);
    }

    [Fact]
    public void Indeterminate_WhenActualMissing()
    {
        var result = ExecutionEngine.ClassifyRecord(Study(new DateTime(2026, 4, 10)), null, "studyId");
        Assert.Equal("Indeterminate", result.Classification);
    }

    [Fact]
    public void DelayedDayCount_IsCorrect()
    {
        var result = ExecutionEngine.ClassifyRecord(Study(new DateTime(2026, 4, 10)), new DateTime(2026, 4, 15), "studyId");
        Assert.Contains("5 day(s)", result.Reason);
    }

    [Fact]
    public void StudyId_IsMappedFromIdField()
    {
        var result = ExecutionEngine.ClassifyRecord(Study(new DateTime(2026, 4, 10)), null, "studyId");
        Assert.Equal("S1", result.StudyId);
    }
}
