using ElimsInsightAssistant.Api.Audit;
using ElimsInsightAssistant.Api.Execution;
using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;
using ElimsInsightAssistant.Api.Validation;
using Microsoft.AspNetCore.Mvc;

namespace ElimsInsightAssistant.Api.Controllers;

[ApiController]
[Route("api/assistant")]
public class AssistantController(
    IPlanGenerator planGenerator,
    IPlanValidator validator,
    IExecutionEngine executionEngine,
    IAuditService auditService,
    IServiceRegistry serviceRegistry) : ControllerBase
{
    [HttpGet("contracts")]
    public IActionResult GetContracts() => Ok(serviceRegistry.GetAll());

    [HttpPost("contracts")]
    public IActionResult RegisterContract([FromBody] ServiceContractEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Name))
            return BadRequest("Name is required.");
        if (string.IsNullOrWhiteSpace(entry.Action))
            return BadRequest("Action is required.");
        if (entry.Fields == null || entry.Fields.Count == 0)
            return BadRequest("At least one field is required.");
        if (string.IsNullOrWhiteSpace(entry.Purpose))
            return BadRequest("Purpose is required (used in AI prompt).");

        serviceRegistry.Register(entry);
        return Ok(serviceRegistry.GetAll());
    }


    [HttpPost("query")]
    public async Task<ActionResult<AssistantQueryResponse>> Query([FromBody] NaturalLanguageQueryRequest request)
    {
        var result = await planGenerator.GenerateAsync(request.Query);

        // Transient server error (network, provider outage) → 503 so clients can retry
        if (result.IsServerError)
            return StatusCode(503, new { status = "ServiceUnavailable", message = result.Error });

        // Genuinely unsupported query → 200 with status flag (not an error, just out of scope)
        if (result.Plan is null)
            return Ok(new AssistantQueryResponse { Status = "UnsupportedQuery", PlanGeneratorMode = planGenerator.ProviderName, Message = result.Error ?? "Unsupported query" });

        var validation = validator.Validate(result.Plan);
        validation = validation with { Checks = [.. validation.Checks, new("User authorization", "Passed")] };
        if (validation.Status != "Passed")
            return BadRequest(validation);

        var traceId = $"TRACE-{Guid.NewGuid():N}";
        var planId  = $"PLAN-{Guid.NewGuid():N}";
        var started = DateTime.UtcNow;
        var (summary, rows, servicesCalled) = await executionEngine.ExecuteAsync(result.Plan, request.UserContext);
        var finished = DateTime.UtcNow;

        var response = new AssistantQueryResponse
        {
            PlanId             = planId,
            TraceId            = traceId,
            PlanGeneratorMode  = planGenerator.ProviderName,
            MarkdownPlan       = result.Markdown,
            JsonPlan           = result.Plan,
            Validation         = validation,
            Summary            = summary,
            Results            = rows
        };

        auditService.Save(new AuditRecord
        {
            TraceId             = traceId,
            PlanId              = planId,
            OriginalQuery       = request.Query,
            UserId              = request.UserContext.UserId,
            MarkdownPlan        = result.Markdown,
            JsonPlan            = result.Plan,
            ValidationStatus    = validation.Status,
            ValidationChecks    = validation.Checks,
            ServicesCalled      = servicesCalled,
            ExecutionStartedAt  = started,
            ExecutionCompletedAt = finished,
            ResultSummary       = summary,
            ResultSnapshot      = rows
        });

        return Ok(response);
    }

    [HttpPost("plan")]
    public async Task<ActionResult<object>> Plan([FromBody] NaturalLanguageQueryRequest request)
    {
        var result = await planGenerator.GenerateAsync(request.Query);
        if (result.IsServerError)
            return StatusCode(503, new { status = "ServiceUnavailable", message = result.Error });
        if (result.Plan is null)
            return Ok(new { status = "UnsupportedQuery", message = result.Error });
        return Ok(new { markdownPlan = result.Markdown, jsonPlan = result.Plan });
    }

    [HttpPost("plan/validate")]
    public ActionResult<ValidationResult> Validate([FromBody] ExecutionPlan plan) =>
        Ok(validator.Validate(plan));

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
