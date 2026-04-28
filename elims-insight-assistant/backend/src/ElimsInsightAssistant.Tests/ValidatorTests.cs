using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Validation;

namespace ElimsInsightAssistant.Tests;

public class ValidatorTests
{
    private readonly PlanValidator _validator = new();

    private static ExecutionPlan ApprovedPlan => new()
    {
        Intent = "find_studies_not_completed_on_time",
        Operations =
        [
            new("study-service", "listStudies", ["studyId", "studyCode", "customer", "legalEntity", "plannedCompletionDate"], []),
            new("corelabs-service", "listTestPs", ["testpId", "studyId", "status", "completedAt", "runType", "result"], [new("status","=","Completed")])
        ],
        Limits = new PlanLimits(500, true)
    };

    [Fact] public void AcceptsApprovedPlan() => Assert.Equal("Passed", _validator.Validate(ApprovedPlan).Status);

    [Fact]
    public void RejectsUnapprovedService()
    {
        var plan = ApprovedPlan with { Operations = [new("evil-service","listStudies",["studyId"],[])] };
        Assert.Equal("Failed", _validator.Validate(plan).Status);
    }

    [Fact]
    public void RejectsUnapprovedField()
    {
        var plan = ApprovedPlan with { Operations = [new("study-service","listStudies",["studyId","secretField"],[])] };
        Assert.Equal("Failed", _validator.Validate(plan).Status);
    }

    [Fact]
    public void RejectsWriteOperation()
    {
        var plan = ApprovedPlan with { Operations = [new("study-service","deleteStudies",["studyId"],[])] };
        Assert.Equal("Failed", _validator.Validate(plan).Status);
    }

    [Fact]
    public void RejectsMissingMaxRows()
    {
        var plan = ApprovedPlan with { Limits = new PlanLimits(0, true) };
        Assert.Equal("Failed", _validator.Validate(plan).Status);
    }

    [Fact]
    public void RejectsMaxRowsOver500()
    {
        var plan = ApprovedPlan with { Limits = new PlanLimits(501, true) };
        Assert.Equal("Failed", _validator.Validate(plan).Status);
    }
}
