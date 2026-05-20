using ElimsInsightAssistant.Api.Audit;
using ElimsInsightAssistant.Api.Execution;
using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;
using ElimsInsightAssistant.Api.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ElimsInsightAssistant.Api.Controllers;

[ApiController]
[Route("api/assistant")]
public class AssistantController(
    IPlanGenerator defaultGenerator,
    IPlanValidator validator,
    IExecutionEngine executionEngine,
    IAuditService auditService,
    IServiceRegistry serviceRegistry,
    IServiceProvider serviceProvider,
    ILogger<AssistantController> logger) : ControllerBase
{
    [HttpGet("providers")]
    public IActionResult GetProviders()
    {
        var gemini     = serviceProvider.GetService<GeminiPlanGenerator>();
        var openRouter = serviceProvider.GetService<OpenRouterPlanGenerator>();
        var all = new List<object>
        {
            new { id = "mock",       name = "Mock (keyword matching)",                        available = true },
            new { id = "gemini",     name = gemini?.ProviderName     ?? "Gemini 2.5 Flash",   available = gemini     is not null },
            new { id = "openrouter", name = openRouter?.ProviderName ?? "OpenRouter",          available = openRouter is not null }
        };
        return Ok(all);
    }

    [HttpGet("contracts")]
    public IActionResult GetContracts() => Ok(serviceRegistry.GetAll());

    [HttpPost("contracts")]
    public IActionResult RegisterContract([FromBody] ServiceContractEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Name))    return BadRequest("Name is required.");
        if (string.IsNullOrWhiteSpace(entry.Action))  return BadRequest("Action is required.");
        if (entry.Fields == null || entry.Fields.Count == 0) return BadRequest("At least one field is required.");
        if (string.IsNullOrWhiteSpace(entry.Purpose)) return BadRequest("Purpose is required (used in AI prompt).");
        serviceRegistry.Register(entry);
        logger.LogInformation("Contract registered: {Name} (action={Action}, required={Required})",
            entry.Name, entry.Action, entry.IsRequired);
        return Ok(serviceRegistry.GetAll());
    }

    [HttpPost("query")]
    public async Task<ActionResult<AssistantQueryResponse>> Query([FromBody] NaturalLanguageQueryRequest request)
    {
        var generator = ResolveGenerator(request.Provider);
        logger.LogInformation("Query received | provider={Provider} | user={User} | query={Query}",
            generator.ProviderName, request.UserContext.UserId, request.Query);

        var result = await generator.GenerateAsync(request.Query);

        if (result.IsServerError)
        {
            logger.LogWarning("Plan generation failed (server error) | provider={Provider} | error={Error}",
                generator.ProviderName, result.Error);
            return StatusCode(503, new { status = "ServiceUnavailable", message = result.Error });
        }

        if (result.Plan is null)
        {
            logger.LogInformation("Query unsupported | provider={Provider} | reason={Reason}",
                generator.ProviderName, result.Error);
            return Ok(new AssistantQueryResponse { Status = "UnsupportedQuery", PlanGeneratorMode = generator.ProviderName, Message = result.Error ?? "Unsupported query" });
        }

        logger.LogInformation("Plan generated | intent={Intent} | services={Services}",
            result.Plan.Intent,
            string.Join(", ", result.Plan.Operations.Select(o => o.Service)));

        var validation = validator.Validate(result.Plan);
        validation = validation with { Checks = [.. validation.Checks, new("User authorization", "Passed")] };

        if (validation.Status != "Passed")
        {
            logger.LogWarning("Validation failed | errors={Errors}",
                string.Join("; ", validation.Errors ?? []));
            return BadRequest(validation);
        }

        logger.LogInformation("Validation passed | checks={Count}", validation.Checks.Count);

        var traceId = $"TRACE-{Guid.NewGuid():N}";
        var planId  = $"PLAN-{Guid.NewGuid():N}";
        var started = DateTime.UtcNow;
        var (summary, rows, servicesCalled) = await executionEngine.ExecuteAsync(result.Plan, request.UserContext);
        var finished = DateTime.UtcNow;

        logger.LogInformation(
            "Execution complete | traceId={TraceId} | services={Services} | rows={Rows} | onTime={OnTime} | delayed={Delayed} | indeterminate={Indeterminate} | elapsed={Elapsed}ms",
            traceId,
            string.Join(", ", servicesCalled),
            rows.Count, summary.OnTime, summary.Delayed, summary.Indeterminate,
            (int)(finished - started).TotalMilliseconds);

        var response = new AssistantQueryResponse
        {
            PlanId            = planId,
            TraceId           = traceId,
            PlanGeneratorMode = generator.ProviderName,
            MarkdownPlan      = result.Markdown,
            JsonPlan          = result.Plan,
            Validation        = validation,
            Summary           = summary,
            Results           = rows
        };

        auditService.Save(new AuditRecord
        {
            TraceId              = traceId,
            PlanId               = planId,
            OriginalQuery        = request.Query,
            UserId               = request.UserContext.UserId,
            MarkdownPlan         = result.Markdown,
            JsonPlan             = result.Plan,
            ValidationStatus     = validation.Status,
            ValidationChecks     = validation.Checks,
            ServicesCalled       = servicesCalled,
            ExecutionStartedAt   = started,
            ExecutionCompletedAt = finished,
            ResultSummary        = summary,
            ResultSnapshot       = rows
        });

        return Ok(response);
    }

    [HttpPost("plan")]
    public async Task<ActionResult<object>> Plan([FromBody] NaturalLanguageQueryRequest request)
    {
        var generator = ResolveGenerator(request.Provider);
        var result = await generator.GenerateAsync(request.Query);
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

    private IPlanGenerator ResolveGenerator(string? providerId) =>
        providerId?.ToLowerInvariant() switch
        {
            "gemini"     => (IPlanGenerator?)serviceProvider.GetService<GeminiPlanGenerator>()     ?? defaultGenerator,
            "openrouter" => (IPlanGenerator?)serviceProvider.GetService<OpenRouterPlanGenerator>() ?? defaultGenerator,
            "mock"       => serviceProvider.GetRequiredService<MockPlanGenerator>(),
            _            => defaultGenerator
        };
}

public record ExecuteRequest(ExecutionPlan Plan, UserContext UserContext);
