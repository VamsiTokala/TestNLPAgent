using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ElimsInsightAssistant.Api.Controllers;

[ApiController]
[Route("api/demo/corelabs/testps")]
public class CoreLabsDemoController(ICoreLabsServiceClient coreLabsService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TestPDto>>> Get() => Ok(await coreLabsService.ListTestPsAsync());
}
