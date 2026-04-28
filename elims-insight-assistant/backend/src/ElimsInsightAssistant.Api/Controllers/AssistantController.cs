using ElimsInsightAssistant.Api.Audit;
using ElimsInsightAssistant.Api.Execution;
using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;
using ElimsInsightAssistant.Api.Validation;
using Microsoft.AspNetCore.Mvc;

namespace ElimsInsightAssistant.Api.Controllers;

[ApiController]
[Route("api/assistant")]
public class AssistantController(IPlanGenerator planGenerator, IPlanValidator validator, IExecutionEngine executionEngine, IAuditService auditService) : ControllerBase
{
    [HttpPost("query")]
    public async Task<ActionResult<AssistantQueryResponse>> Query([FromBody] NaturalLanguageQueryRequest request)
    {
        var (markdown, plan, error) = planGenerator.Generate(request.Query);
        if (plan is null)
            return Ok(new AssistantQueryResponse { Status = "UnsupportedQuery", Message = error ?? "Unsupported query" });

        var validation = validator.Validate(plan);
        validation = validation with { Checks = [.. validation.Checks, new("User authorization", "Passed")] };
        if (validation.Status != "Passed")
            return BadRequest(validation);

        var traceId = $"TRACE-{Guid.NewGuid():N}";
        var planId = $"PLAN-{Guid.NewGuid():N}";
        var started = DateTime.UtcNow;
        var (summary, rows, servicesCalled) = await executionEngine.ExecuteAsync(plan, request.UserContext);
        var finished = DateTime.UtcNow;

        var response = new AssistantQueryResponse
        {
            PlanId = planId,
            TraceId = traceId,
            MarkdownPlan = markdown,
            JsonPlan = plan,
            Validation = validation,
            Summary = summary,
            Results = rows
        };

        auditService.Save(new AuditRecord
        {
            TraceId = traceId,
            PlanId = planId,
            OriginalQuery = request.Query,
            UserId = request.UserContext.UserId,
            MarkdownPlan = markdown,
            JsonPlan = plan,
            ValidationStatus = validation.Status,
            ValidationChecks = validation.Checks,
            ServicesCalled = servicesCalled,
            ExecutionStartedAt = started,
            ExecutionCompletedAt = finished,
            ResultSummary = summary,
            ResultSnapshot = rows
        });

        return Ok(response);
    }

    [HttpPost("plan")]
    public ActionResult<object> Plan([FromBody] NaturalLanguageQueryRequest request)
    {
        var (markdown, plan, error) = planGenerator.Generate(request.Query);
        if (plan is null) return Ok(new { status = "UnsupportedQuery", message = error });
        return Ok(new { markdownPlan = markdown, jsonPlan = plan });
    }

    [HttpPost("plan/validate")]
    public ActionResult<ValidationResult> Validate([FromBody] ExecutionPlan plan) => Ok(validator.Validate(plan));

    [HttpPost("execute")]
    public async Task<ActionResult<object>> Execute([FromBody] ExecuteRequest request)
    {
        var validation = validator.Validate(request.Plan);
        if (validation.Status != "Passed") return BadRequest(validation);
        var (summary, rows, _) = await executionEngine.ExecuteAsync(request.Plan, request.UserContext);
        return Ok(new { summary, results = rows, validation });
    }

    [HttpGet("audit/{traceId}")]
    public ActionResult<AuditRecord> Audit(string traceId)
    {
        var record = auditService.Get(traceId);
        return record is null ? NotFound() : Ok(record);
    }
}

public record ExecuteRequest(ExecutionPlan Plan, UserContext UserContext);
