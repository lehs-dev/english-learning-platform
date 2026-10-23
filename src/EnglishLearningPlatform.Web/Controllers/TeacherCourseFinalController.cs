using System.Security.Claims;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Web.Models.Assessment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Teacher)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherCourseFinalController(ITeacherCourseFinalService service) : Controller
{
    // Course và owner không được lấy từ các hidden input do client gửi lên.
    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private IActionResult Denied(LearningAccessResult access) => access == LearningAccessResult.NotFound ? NotFound() : Forbid();

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        if (!ModelState.IsValid || id == Guid.Empty) return BadRequest();
        var result = await service.GetEditorAsync(ActorId, id, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        if (result.Value == null) return NotFound();
        return View(new TeacherCourseFinalViewModel
        {
            Editor = result.Value,
            Input = new CourseFinalRequest
            {
                FinalAssessmentId = result.Value.FinalAssessmentId,
                ExpectedFinalAssessmentId = result.Value.FinalAssessmentId,
                ExpectedUpdatedAtUtc = result.Value.UpdatedAtUtc
            }
        });
    }

    /// <summary>POST có CSRF. Khi lỗi, giữ version cũ để tab cũ không tự được nâng quyền ghi đè.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, [Bind(Prefix = "Input")] CourseFinalRequest input, CancellationToken ct)
    {
        if (id == Guid.Empty || input == null) return BadRequest();
        var editor = await service.GetEditorAsync(ActorId, id, ct);
        if (editor.Access != LearningAccessResult.Allowed) return Denied(editor.Access);
        if (editor.Value == null) return NotFound();
        if (ModelState.IsValid)
        {
            var result = await service.SaveAsync(ActorId, id, input, ct);
            if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
            if (result.Success)
            {
                TempData["Success"] = "Đã lưu Final và cập nhật điều kiện hoàn thành Course.";
                return RedirectToAction(nameof(Edit), new { id });
            }
            foreach (var error in result.Errors)
                foreach (string message in error.Value)
                    ModelState.AddModelError(string.IsNullOrEmpty(error.Key) ? "" : "Input." + error.Key, message);
            // Student có thể vừa Start trong lúc lưu: đọc lại để hiển thị trạng thái khóa mới.
            editor = await service.GetEditorAsync(ActorId, id, ct);
            if (editor.Access != LearningAccessResult.Allowed) return Denied(editor.Access);
            if (editor.Value == null) return NotFound();
        }
        return View(new TeacherCourseFinalViewModel { Editor = editor.Value, Input = input });
    }
}
