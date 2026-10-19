using System.Security.Claims;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Teacher)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class TeacherAssessmentPreviewController : Controller
{
    private readonly ITeacherAssessmentPreviewService _service;

    public TeacherAssessmentPreviewController(
        ITeacherAssessmentPreviewService service)
    {
        if (service == null)
        {
            throw new ArgumentNullException(nameof(service));
        }

        _service = service;
    }

    // GET chỉ mở đề đã lưu; không bắt đầu lượt làm bài.
    [HttpGet]
    public async Task<IActionResult> Index(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || id == Guid.Empty)
        {
            return BadRequest();
        }

        // Owner không lấy từ query string hoặc form.
        bool validActor = Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out Guid actorUserId);

        if (!validActor || actorUserId == Guid.Empty)
        {
            return Challenge();
        }

        var result = await _service.GetAsync(
            actorUserId,
            id,
            cancellationToken);

        if (result.Access == LearningAccessResult.NotFound)
        {
            return NotFound();
        }

        if (result.Access != LearningAccessResult.Allowed)
        {
            return Forbid();
        }

        if (result.Value == null)
        {
            return NotFound();
        }

        return View(result.Value);
    }
}