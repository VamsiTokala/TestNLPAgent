using ElimsInsightAssistant.Api.Execution;
using ElimsInsightAssistant.Api.Models;

namespace ElimsInsightAssistant.Tests;

// Classification logic lives in ExecutionEngine.ClassifyRecord (internal static).
// It returns a generic Dictionary<string, object?> so the UI can render dynamic columns.

public class ClassificationTests
{
    private static StudyDto Study(DateTime? planned) =>
        new("S1", "ST-001", "Acme", "EU", planned);

    private static string Class(Dictionary<string, object?> row) =>
        row["classification"]?.ToString() ?? "";

    [Fact]
    public void OnTime_WhenActualBeforePlanned()
    {
        var s = Study(new DateTime(2026, 4, 10));
        var result = ExecutionEngine.ClassifyRecord(s, s, new DateTime(2026, 4, 9));
        Assert.Equal("On Time", Class(result));
    }

    [Fact]
    public void OnTime_WhenActualEqualsPlanned()
    {
        var s = Study(new DateTime(2026, 4, 10));
        var result = ExecutionEngine.ClassifyRecord(s, s, new DateTime(2026, 4, 10));
        Assert.Equal("On Time", Class(result));
    }

    [Fact]
    public void Delayed_WhenActualAfterPlanned()
    {
        var s = Study(new DateTime(2026, 4, 10));
        var result = ExecutionEngine.ClassifyRecord(s, s, new DateTime(2026, 4, 12));
        Assert.Equal("Delayed", Class(result));
    }

    [Fact]
    public void Indeterminate_WhenPlannedMissing()
    {
        var s = Study(null);
        var result = ExecutionEngine.ClassifyRecord(s, s, new DateTime(2026, 4, 12));
        Assert.Equal("Indeterminate", Class(result));
    }

    [Fact]
    public void Indeterminate_WhenActualMissing()
    {
        var s = Study(new DateTime(2026, 4, 10));
        var result = ExecutionEngine.ClassifyRecord(s, s, null);
        Assert.Equal("Indeterminate", Class(result));
    }

    [Fact]
    public void DelayedDayCount_IsCorrect()
    {
        var s = Study(new DateTime(2026, 4, 10));
        var result = ExecutionEngine.ClassifyRecord(s, s, new DateTime(2026, 4, 15));
        Assert.Contains("5 day(s)", result["reason"]?.ToString());
    }

    [Fact]
    public void Row_IncludesRecordFields()
    {
        var s = Study(new DateTime(2026, 4, 10));
        var result = ExecutionEngine.ClassifyRecord(s, s, null);
        Assert.Equal("S1", result["studyId"]);
        Assert.Equal("ST-001", result["studyCode"]);
    }
}
