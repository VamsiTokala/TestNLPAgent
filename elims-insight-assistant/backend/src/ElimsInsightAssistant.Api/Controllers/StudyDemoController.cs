using ElimsInsightAssistant.Api.Models;
using ElimsInsightAssistant.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ElimsInsightAssistant.Api.Controllers;

[ApiController]
[Route("api/demo/studies")]
public class StudyDemoController(IStudyServiceClient studyService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<StudyDto>>> Get() => Ok(await studyService.ListStudiesAsync());
}
