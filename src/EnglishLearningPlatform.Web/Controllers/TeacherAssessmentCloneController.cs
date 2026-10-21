using System.Security.Claims;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Teacher)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherAssessmentCloneController(ITeacherAssessmentCloneService service) : Controller
{
    // Chỉ POST có CSRF mới tạo bản sao; GET/F5 danh sách không tạo dữ liệu.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Guid id, DateTimeOffset? expectedUpdatedAtUtc, CancellationToken ct)
    {
        if (!ModelState.IsValid || id == Guid.Empty) return BadRequest();
        Guid actor = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await service.CloneAsync(actor, id, expectedUpdatedAtUtc, ct);
        if (result.Access != LearningAccessResult.Allowed)
            return result.Access == LearningAccessResult.NotFound ? NotFound() : Forbid();
        if (!result.Success)
        {
            TempData["Error"] = string.Join("\n", result.Errors.Values.SelectMany(messages => messages));
            return RedirectToAction("Index", "TeacherAssessments");
        }
        TempData["Success"] = "Đã tạo bản sao Draft, giữ Course của bản gốc nhưng không tự thay Final. "
            + "Question/Stimulus được dùng lại và vẫn khóa; muốn đổi nội dung hãy tạo Question/Stimulus mới.";
        return RedirectToAction("Edit", "TeacherAssessments", new { id = result.Id });
    }
}
