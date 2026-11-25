using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.TeacherAnalytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Teacher)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherAnalyticsController(ITeacherAnalyticsQueryService analytics) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(TeacherAnalyticsQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest();
        var result = await analytics.GetAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), query, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Forbid();
    }
}
