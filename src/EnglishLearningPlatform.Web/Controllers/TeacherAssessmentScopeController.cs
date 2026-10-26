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
public sealed class TeacherAssessmentScopeController : Controller
{
    private readonly ITeacherAssessmentScopeService _service;

    public TeacherAssessmentScopeController(ITeacherAssessmentScopeService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    /// <summary>
    /// Chỉ đọc phạm vi hiện tại. Đề đã có Attempt vẫn xem được lý do bị khóa.
    /// GET không đổi Course và không tạo Attempt.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out Guid actorUserId))
        {
            return Challenge();
        }
        if (!ModelState.IsValid || id == Guid.Empty)
        {
            return BadRequest();
        }

        var result = await _service.GetEditorAsync(actorUserId, id, cancellationToken);
        if (result.Access != LearningAccessResult.Allowed)
        {
            return Denied(result.Access);
        }
        if (result.Value == null)
        {
            return NotFound();
        }

        return View(new TeacherAssessmentScopeViewModel
        {
            Editor = result.Value,
            Input = new AssessmentScopeRequest
            {
                CourseId = result.Value.CourseId,
                ExpectedUpdatedAtUtc = result.Value.UpdatedAtUtc
            }
        });
    }

    /// <summary>
    /// Server kiểm tra lại quyền, version, Attempt và Final khi lưu.
    /// Chỉ bind request phạm vi, không dùng Assessment entity làm form input.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        [Bind(Prefix = "Input")] AssessmentScopeRequest input,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out Guid actorUserId))
        {
            return Challenge();
        }
        if (id == Guid.Empty || input == null)
        {
            return BadRequest();
        }

        // Kiểm tra owner trước khi trả lại form, kể cả khi MVC phát hiện input sai kiểu.
        var editor = await _service.GetEditorAsync(actorUserId, id, cancellationToken);
        if (editor.Access != LearningAccessResult.Allowed)
        {
            return Denied(editor.Access);
        }
        if (editor.Value == null)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            var result = await _service.SaveAsync(actorUserId, id, input, cancellationToken);
            if (result.Access != LearningAccessResult.Allowed)
            {
                return Denied(result.Access);
            }
            if (result.Success)
            {
                TempData["Success"] = "Đã lưu phạm vi truy cập của bài kiểm tra.";
                // GET mới lấy version mới; refresh không gửi lại POST đổi phạm vi.
                return RedirectToAction(nameof(Edit), new { id });
            }

            foreach (var error in result.Errors)
            {
                string field = string.IsNullOrEmpty(error.Key) ? string.Empty : "Input." + error.Key;
                foreach (string message in error.Value)
                {
                    ModelState.AddModelError(field, message);
                }
            }

            // Một Student có thể vừa Start hoặc tab khác vừa lưu trong lúc xử lý.
            // Đọc lại để trang hiển thị đúng phạm vi/lý do khóa hiện tại.
            editor = await _service.GetEditorAsync(actorUserId, id, cancellationToken);
            if (editor.Access != LearningAccessResult.Allowed)
            {
                return Denied(editor.Access);
            }
            if (editor.Value == null)
            {
                return NotFound();
            }
        }

        // Giữ version Teacher đã gửi khi có lỗi, không tự nâng version của tab cũ.
        // Teacher phải tải lại trang trước khi gửi lựa chọn mới.
        return View(new TeacherAssessmentScopeViewModel { Editor = editor.Value, Input = input });
    }

    private bool TryGetActorUserId(out Guid actorUserId)
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out actorUserId)
            && actorUserId != Guid.Empty;
    }

    private IActionResult Denied(LearningAccessResult access)
    {
        return access == LearningAccessResult.NotFound ? NotFound() : Forbid();
    }
}
