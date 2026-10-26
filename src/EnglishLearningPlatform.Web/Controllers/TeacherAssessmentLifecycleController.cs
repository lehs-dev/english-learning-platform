using System.Security.Claims;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Teacher)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherAssessmentLifecycleController(ITeacherAssessmentLifecycleService service) : Controller
{
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Unpublish(Guid id, DateTimeOffset? expectedUpdatedAtUtc, CancellationToken ct) =>
        Change(id, AssessmentStatus.Unpublished, expectedUpdatedAtUtc, ct);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Archive(Guid id, DateTimeOffset? expectedUpdatedAtUtc, CancellationToken ct) =>
        Change(id, AssessmentStatus.Archived, expectedUpdatedAtUtc, ct);

    // Hai action không nhận trạng thái tùy ý từ form; đích do server quyết định.
    private async Task<IActionResult> Change(Guid id, AssessmentStatus target, DateTimeOffset? version, CancellationToken ct)
    {
        if (!ModelState.IsValid || id == Guid.Empty) return BadRequest();
        Guid actor = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await service.ChangeStatusAsync(actor, id, target, version, ct);
        if (result.Access != LearningAccessResult.Allowed)
            return result.Access == LearningAccessResult.NotFound ? NotFound() : Forbid();
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? "Đã đổi trạng thái đề. Các lượt làm và kết quả hiện có được giữ nguyên."
            : string.Join("\n", result.Errors.Values.SelectMany(messages => messages));
        return RedirectToAction("Index", "TeacherAssessments");
    }
}
