using System.Security.Claims;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
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
    private readonly ITeacherAssessmentPublishService _publishService;

    public TeacherAssessmentsController(
        ITeacherAssessmentDraftService service,
        ITeacherAssessmentPublishService publishService)
    {
        if (service == null)
        {
            throw new ArgumentNullException(nameof(service));
        }

        if (publishService == null)
        {
            throw new ArgumentNullException(nameof(publishService));
        }

        _service = service;
        _publishService = publishService;
    }

    /// <summary>
    /// Mở danh sách Assessment của Teacher hiện tại.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken,
        string? search = null,
        AssessmentStatus? status = null,
        AssessmentType? type = null)
    {
        if (!TryGetActorUserId(out Guid actorUserId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid
            || (status.HasValue && !Enum.IsDefined(status.Value))
            || (type.HasValue && !Enum.IsDefined(type.Value)))
        {
            return BadRequest();
        }

        var result = await _service.GetListAsync(
            actorUserId, cancellationToken);

        if (result.Access != LearningAccessResult.Allowed)
        {
            return Denied(result.Access);
        }

        var all = result.Value ?? [];
        search = search?.Trim();
        var items = all.Where(item =>
            (string.IsNullOrEmpty(search) || item.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
            && (!status.HasValue || item.Status == status)
            && (!type.HasValue || item.AssessmentType == type)).ToList();

        return View(new TeacherAssessmentIndexViewModel
        {
            Items = items,
            TotalCount = all.Count,
            StatusCounts = Enum.GetValues<AssessmentStatus>().ToDictionary(value => value,
                value => all.Count(item => item.Status == value)),
            Search = search,
            Status = status,
            Type = type
        });
    }

    /// <summary>
    /// Không có id: mở form tạo.
    /// Có id: đọc cấu hình Draft/Published thuộc Teacher khi chưa có Attempt.
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
            var result = await _service.GetConfigurationEditorAsync(
                actorUserId, id.Value, cancellationToken);

            if (result.Access != LearningAccessResult.Allowed)
            {
                return Denied(result.Access);
            }

            if (result.Value == null)
            {
                return NotFound();
            }

            model.Input = result.Value.Input;
            model.Status = result.Value.Status;
        }

        return View(model);
    }

    /// <summary>
    /// Nhận cấu hình; trạng thái và quyền sửa luôn đọc từ server.
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
        AssessmentStatus status = AssessmentStatus.Draft;

        if (id.HasValue)
        {
            var access = await _service.GetConfigurationEditorAsync(
                actorUserId, id.Value, cancellationToken);

            if (access.Access != LearningAccessResult.Allowed)
            {
                return Denied(access.Access);
            }

            if (access.Value == null)
            {
                return NotFound();
            }

            status = access.Value.Status;
        }

        var model = new TeacherAssessmentDraftViewModel
        {
            Id = id,
            Input = input,
            Status = status
        };

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _service.SaveConfigurationAsync(
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
            status == AssessmentStatus.Published
                ? "Đã cập nhật cấu hình. Bài kiểm tra vẫn đang mở."
                : "Đã lưu bản nháp bài kiểm tra.";

        // Redirect sau khi lưu để refresh không gửi lại POST.
        // GET mới cũng lấy lại version vừa được cập nhật.
        return RedirectToAction(
            nameof(Edit),
            new { id = result.Id });
    }

    /// <summary>
    /// Công bố đề đã lưu.
    /// Không lưu kèm những chỉnh sửa chưa được submit ở form cấu hình.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(
        Guid id,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out Guid actorUserId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid || id == Guid.Empty)
        {
            return BadRequest();
        }

        var result = await _publishService.PublishAsync(
            actorUserId,
            id,
            expectedUpdatedAtUtc,
            cancellationToken);

        if (result.Access != LearningAccessResult.Allowed)
        {
            return Denied(result.Access);
        }

        if (result.Success)
        {
            TempData["Success"] =
                "Đã mở bài kiểm tra và khóa nội dung câu hỏi, ngữ liệu.";

            // Draft đã thành Published nên quay về danh sách.
            return RedirectToAction(nameof(Index));
        }

        // Đọc lại Draft để hiển thị cấu hình thật cùng các lỗi Publish.
        var draft = await _service.GetDraftAsync(
            actorUserId,
            id,
            cancellationToken);

        if (draft.Access == LearningAccessResult.NotFound)
        {
            return NotFound();
        }

        if (draft.Access != LearningAccessResult.Allowed
            || draft.Value == null)
        {
            // Có thể đề vừa được Publish ở tab khác.
            // Không tự đưa Published trở lại Draft.
            TempData["Error"] =
                "Không thể mở bài kiểm tra từ trang hiện tại. "
                + "Hãy kiểm tra lại trạng thái trong danh sách.";

            return RedirectToAction(nameof(Index));
        }

        var model = new TeacherAssessmentDraftViewModel
        {
            Id = id,
            Input = draft.Value
        };

        // Hiển thị toàn bộ lỗi bằng validation-summary đã có.
        AddErrors(result);

        return View("Edit", model);
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
