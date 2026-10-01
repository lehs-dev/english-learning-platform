using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Authorization;
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

    // Probe kiểm tra tay các authorization policy bằng browser:
    // chưa đăng nhập -> 401; đăng nhập sai role hoặc nhiều hơn một role -> 403.
    [HttpGet("policy/teacher")]
    [Authorize(Policy = AuthorizationPolicies.Teacher)]
    public IActionResult TeacherPolicyProbe()
        => Ok(new { policy = AuthorizationPolicies.Teacher, user = User.Identity?.Name });

    [HttpGet("policy/single-active-role")]
    [Authorize(Policy = AuthorizationPolicies.SingleActiveRole)]
    public IActionResult SingleActiveRolePolicyProbe()
        => Ok(new { policy = AuthorizationPolicies.SingleActiveRole, user = User.Identity?.Name });
}
