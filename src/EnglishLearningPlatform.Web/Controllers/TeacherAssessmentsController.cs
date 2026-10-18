using System.Security.Claims;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Web.Models.Assessment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Teacher)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class TeacherAssessmentsController : Controller
{
    private readonly ITeacherAssessmentDraftService _service;

    public TeacherAssessmentsController(
        ITeacherAssessmentDraftService service)
    {
        if (service == null)
        {
            throw new ArgumentNullException(nameof(service));
        }

        _service = service;
    }

    /// <summary>
    /// Mở danh sách Assessment của Teacher hiện tại.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out Guid actorUserId))
        {
            return Challenge();
        }

        var result = await _service.GetListAsync(
            actorUserId, cancellationToken);

        if (result.Access != LearningAccessResult.Allowed)
        {
            return Denied(result.Access);
        }

        return View(result.Value);
    }

    /// <summary>
    /// Không có id: mở form tạo.
    /// Có id: đọc Draft thuộc Teacher để sửa.
    /// GET không tạo hoặc cập nhật dữ liệu.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Edit(
        Guid? id,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out Guid actorUserId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return BadRequest();
        }

        var model = new TeacherAssessmentDraftViewModel
        {
            Id = id
        };

        if (id.HasValue)
        {
            var result = await _service.GetDraftAsync(
                actorUserId, id.Value, cancellationToken);

            if (result.Access != LearningAccessResult.Allowed)
            {
                return Denied(result.Access);
            }

            if (result.Value == null)
            {
                return NotFound();
            }

            model.Input = result.Value;
        }

        return View(model);
    }

    /// <summary>
    /// Nhận cấu hình Draft.
    /// Antiforgery bảo vệ thao tác ghi từ form.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid? id,
        [Bind(Prefix = "Input")] AssessmentDraftRequest input,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out Guid actorUserId))
        {
            return Challenge();
        }

        if (input == null)
        {
            return BadRequest();
        }

        // Kiểm tra owner trước khi hiển thị lại form có id.
        // Service lưu vẫn kiểm tra lần nữa trong transaction.
        if (id.HasValue)
        {
            var access = await _service.GetDraftAsync(
                actorUserId, id.Value, cancellationToken);

            if (access.Access != LearningAccessResult.Allowed)
            {
                return Denied(access.Access);
            }
        }

        var model = new TeacherAssessmentDraftViewModel
        {
            Id = id,
            Input = input
        };

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _service.SaveDraftAsync(
            actorUserId, id, input, cancellationToken);

        if (result.Access != LearningAccessResult.Allowed)
        {
            return Denied(result.Access);
        }

        if (!result.Success)
        {
            AddErrors(result);
            return View(model);
        }

        TempData["Success"] =
            "Đã lưu Assessment Draft. Bài chưa được Publish.";

        // Redirect sau khi lưu để refresh không gửi lại POST.
        // GET mới cũng lấy lại version vừa được cập nhật.
        return RedirectToAction(
            nameof(Edit),
            new { id = result.Id });
    }

    /// <summary>
    /// Lấy ID từ tài khoản đăng nhập, không lấy từ form.
    /// </summary>
    private bool TryGetActorUserId(out Guid actorUserId)
    {
        return Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out actorUserId)
            && actorUserId != Guid.Empty;
    }

    /// <summary>
    /// Gắn lỗi service vào đúng trường Input trên form.
    /// </summary>
    private void AddErrors(WriteOutcome result)
    {
        foreach (var error in result.Errors)
        {
            string field = string.IsNullOrEmpty(error.Key)
                ? string.Empty
                : "Input." + error.Key;

            foreach (string message in error.Value)
            {
                ModelState.AddModelError(field, message);
            }
        }
    }

    private IActionResult Denied(LearningAccessResult access)
    {
        if (access == LearningAccessResult.NotFound)
        {
            return NotFound();
        }

        return Forbid();
    }
}