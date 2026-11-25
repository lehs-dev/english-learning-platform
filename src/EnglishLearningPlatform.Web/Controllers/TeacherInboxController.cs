using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Teacher)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherInboxController(ITeacherInboxService inbox) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] TeacherInboxQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest();

        var teacherId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await inbox.GetAsync(teacherId, query, ct);
        return result.Access switch
        {
            LearningAccessResult.Allowed => View(result.Value),
            LearningAccessResult.NotFound => NotFound(),
            _ => Forbid()
        };
    }
}
