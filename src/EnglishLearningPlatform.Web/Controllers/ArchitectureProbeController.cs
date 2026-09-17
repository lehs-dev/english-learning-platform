using EnglishLearningPlatform.Application.Assessment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[ApiController]
[Route("api/probe")]
public sealed class ArchitectureProbeController(IExamScoringService scoringService) : ControllerBase
{
    [HttpGet("di")]
    [AllowAnonymous]
    public IActionResult DependencyInjectionProbe()
        => Ok(new { service = scoringService.GetType().Name, utc = DateTimeOffset.UtcNow });
}
