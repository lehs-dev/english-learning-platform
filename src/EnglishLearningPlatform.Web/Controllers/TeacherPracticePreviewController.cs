using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Teacher)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherPracticePreviewController(ITeacherPracticePreviewService service) : Controller
{
    // Chỉ có GET. Không nhận đáp án, owner hoặc hành động Publish từ form.
    [HttpGet]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || id == Guid.Empty) return BadRequest();
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid actorUserId))
            return Challenge();
        var result = await service.GetAsync(actorUserId, id, cancellationToken);
        if (result.Access == LearningAccessResult.NotFound) return NotFound();
        if (result.Access != LearningAccessResult.Allowed) return Forbid();
        return result.Value == null ? NotFound() : View(result.Value);
    }
}
