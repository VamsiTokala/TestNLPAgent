using ElimsInsightAssistant.Api.Execution;
using ElimsInsightAssistant.Api.Models;

namespace ElimsInsightAssistant.Tests;

// Classification logic lives in ExecutionEngine.ClassifyRecord (internal static).
// It returns a generic Dictionary<string, object?> so the UI can render dynamic columns.

public class ClassificationTests
{
    private static StudyDto Study(DateTime? planned) =>
        new("S1", "ST-001", "Acme", "DS-BIOANALYTICS", planned);

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

    private static TestPDto Pending(string id = "T1") => new(id, "S1", "Pending", null, "Production", null);
    private static TestPDto Failed(string id = "T2", DateTime? completedAt = null) =>
        new(id, "S1", "Completed", completedAt ?? new DateTime(2026, 4, 15), "Production", "Fail");

    private static List<string> Flags(Dictionary<string, object?> row) =>
        (List<string>)row["dataQualityFlags"]!;

    [Fact]
    public void Indeterminate_WithNoSiblings_ReportsNoTestPRecords()
    {
        var s = Study(new DateTime(2026, 4, 10));
        var result = ExecutionEngine.ClassifyRecord(s, s, null, childRecords: []);
        Assert.Contains("no TestP records exist", result["reason"]?.ToString());
        Assert.Contains("no_testp_records", Flags(result));
    }

    [Fact]
    public void Indeterminate_WithPendingSibling_ReportsPendingInReason()
    {
        var s = Study(new DateTime(2026, 4, 10));
        var result = ExecutionEngine.ClassifyRecord(s, s, null, childRecords: [Pending()]);
        Assert.Contains("1 TestP(s) pending", result["reason"]?.ToString());
        Assert.Contains("pending_testp", Flags(result));
    }

    [Fact]
    public void Indeterminate_WithFailedSibling_ReportsFailedInReason()
    {
        var s = Study(new DateTime(2026, 4, 10));
        var result = ExecutionEngine.ClassifyRecord(s, s, null, childRecords: [Failed()]);
        Assert.Contains("1 TestP(s) failed", result["reason"]?.ToString());
        Assert.Contains("failed_testp", Flags(result));
    }

    [Fact]
    public void Delayed_WithFailedSibling_ReportsFailedInReason()
    {
        var s = Study(new DateTime(2026, 4, 10));
        var result = ExecutionEngine.ClassifyRecord(s, s, new DateTime(2026, 4, 15), childRecords: [Failed()]);
        Assert.Equal("Delayed", Class(result));
        Assert.Contains("1 TestP(s) failed", result["reason"]?.ToString());
        Assert.Contains("failed_testp", Flags(result));
    }

    [Fact]
    public void Delayed_WithFailedAndPendingSiblings_ReportsBothInReason()
    {
        var s = Study(new DateTime(2026, 4, 10));
        var result = ExecutionEngine.ClassifyRecord(s, s, new DateTime(2026, 4, 15),
            childRecords: [Failed(), Pending("T3")]);

        Assert.Equal("Delayed", Class(result));
        var reason = result["reason"]?.ToString() ?? "";
        Assert.Contains("1 TestP(s) failed", reason);
        Assert.Contains("1 TestP(s) still pending", reason);
        Assert.Contains("failed_testp", Flags(result));
        Assert.Contains("pending_testp", Flags(result));
    }
}
