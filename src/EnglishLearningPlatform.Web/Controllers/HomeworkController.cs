using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Homework;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Roles = AppRoles.Student + "," + AppRoles.Teacher, Policy = AuthorizationPolicies.SingleActiveRole)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class HomeworkController(IWritingHomeworkService homework, TimeProvider time) : Controller
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Course(Guid id, int page, CancellationToken ct)
    {
        var result = await homework.GetCourseAsync(UserId, id, page, ct);
        return result.Value is { } course ? View(course) : Denied(result.Access);
    }

    [HttpGet, Authorize(Policy = AuthorizationPolicies.Teacher)]
    public async Task<IActionResult> Create(Guid id, CancellationToken ct)
    {
        var result = await homework.GetAuthoringCourseAsync(UserId, id, ct);
        if (result.Value is not { } course) return Denied(result.Access);
        return course.CanAssign ? AuthoringView(course, new()) : RedirectClosed(id);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AuthorizationPolicies.Teacher)]
    public async Task<IActionResult> Create(Guid id, WritingAssignmentInput input, [FromForm] string? intent, CancellationToken ct)
    {
        var plan = ReadPlan(input);
        var preview = intent == "preview";
        if (ModelState.IsValid && !preview)
        {
            var result = await homework.AssignAsync(UserId, id, plan, ct);
            switch (result.Status)
            {
                case WritingHomeworkWriteStatus.Saved:
                    return PlanSaved(result.AssignmentId, input.PublishMode);
                case WritingHomeworkWriteStatus.Closed:
                    return RedirectClosed(id);
                case WritingHomeworkWriteStatus.Forbidden:
                    return Forbid();
                case WritingHomeworkWriteStatus.NotFound:
                    return NotFound();
                default:
                    ModelState.AddModelError(string.Empty, "Kiểm tra nội dung và thời gian giao bài.");
                    break;
            }
        }

        var access = await homework.GetAuthoringCourseAsync(UserId, id, ct);
        if (access.Value is not { } course) return Denied(access.Access);
        if (!course.CanAssign) return RedirectClosed(id);
        return AuthoringView(course, input, preview && ModelState.IsValid ? plan : null);
    }

    [HttpGet, Authorize(Policy = AuthorizationPolicies.Teacher)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var detail = await homework.GetAssignmentAsync(UserId, id, 1, ct);
        if (detail.Value is not { } assignment) return Denied(detail.Access);
        if (!assignment.CanEdit) return ReadOnlyAssignment(id);

        return EditView(assignment, new WritingAssignmentInput
        {
            Title = assignment.Title,
            Prompt = assignment.Prompt,
            DueAt = assignment.DueAtUtc?.ToOffset(WritingHomeworkLimits.DisplayOffset).DateTime,
            PublishMode = assignment.IsDraft ? WritingAssignmentPublishMode.Draft : WritingAssignmentPublishMode.Scheduled,
            PublishAt = assignment.PublishAtUtc?.ToOffset(WritingHomeworkLimits.DisplayOffset).DateTime
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AuthorizationPolicies.Teacher)]
    public async Task<IActionResult> Edit(Guid id, WritingAssignmentInput input, [FromForm] string? intent, CancellationToken ct)
    {
        var plan = ReadPlan(input);
        var preview = intent == "preview";
        if (ModelState.IsValid && !preview)
        {
            var result = await homework.UpdateAssignmentAsync(UserId, id, plan, ct);
            switch (result.Status)
            {
                case WritingHomeworkWriteStatus.Saved:
                    return PlanSaved(id, input.PublishMode);
                case WritingHomeworkWriteStatus.Closed:
                case WritingHomeworkWriteStatus.AlreadyPublished:
                    return ReadOnlyAssignment(id);
                case WritingHomeworkWriteStatus.Forbidden:
                    return Forbid();
                case WritingHomeworkWriteStatus.NotFound:
                    return NotFound();
                default:
                    ModelState.AddModelError(string.Empty, "Kiểm tra nội dung và thời gian giao bài.");
                    break;
            }
        }

        var detail = await homework.GetAssignmentAsync(UserId, id, 1, ct);
        if (detail.Value is not { } assignment) return Denied(detail.Access);
        if (!assignment.CanEdit) return ReadOnlyAssignment(id);
        return EditView(assignment, input, preview && ModelState.IsValid ? plan : null);
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, int page, CancellationToken ct)
    {
        var result = await homework.GetAssignmentAsync(UserId, id, page, ct);
        return result.Value is { } assignment ? View(assignment) : Denied(result.Access);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AuthorizationPolicies.Student)]
    public async Task<IActionResult> SaveDraft(Guid id, string? response, Guid? draftVersion, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest();
        var result = await homework.SaveDraftAsync(UserId, id, response, draftVersion, ct);
        switch (result.Status)
        {
            case WritingHomeworkWriteStatus.Saved:
                TempData["Success"] = "Đã lưu bản nháp.";
                break;
            case WritingHomeworkWriteStatus.AlreadySubmitted:
                TempData["Success"] = "Bài viết đã được nộp.";
                break;
            case WritingHomeworkWriteStatus.Closed:
                TempData["Error"] = "Bài tập đã đóng.";
                break;
            case WritingHomeworkWriteStatus.Invalid:
                return await ResponseError(id, response, $"Nhập bài viết từ 1 đến {WritingHomeworkLimits.ResponseLength} ký tự.", ct);
            case WritingHomeworkWriteStatus.DraftChanged:
                return await ResponseError(id, response,
                    "Bản nháp đã được cập nhật ở nơi khác. Xem bản đã lưu trước khi lưu lại.", ct, draftChanged: true);
            case WritingHomeworkWriteStatus.NotFound:
                return NotFound();
            default:
                return Forbid();
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AuthorizationPolicies.Student)]
    public async Task<IActionResult> Submit(Guid id, string? response, CancellationToken ct)
    {
        var result = await homework.SubmitAsync(UserId, id, response, ct);
        switch (result.Status)
        {
            case WritingHomeworkWriteStatus.Saved:
            case WritingHomeworkWriteStatus.AlreadySubmitted:
                TempData["Success"] = "Bài viết đã được nộp.";
                break;
            case WritingHomeworkWriteStatus.Closed:
                TempData["Error"] = "Bài tập đã đóng.";
                break;
            case WritingHomeworkWriteStatus.Invalid:
                return await ResponseError(id, response, $"Nhập bài viết từ 1 đến {WritingHomeworkLimits.ResponseLength} ký tự.", ct);
            case WritingHomeworkWriteStatus.NotFound:
                return NotFound();
            default:
                return Forbid();
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AuthorizationPolicies.Teacher)]
    public async Task<IActionResult> Review(Guid id, Guid submissionId, int page, WritingReviewInput input, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await homework.ReviewAsync(UserId, id, submissionId, input.Feedback, input.Score, ct);
            switch (result.Status)
            {
                case WritingHomeworkWriteStatus.Saved:
                case WritingHomeworkWriteStatus.AlreadyReviewed:
                    TempData["Success"] = "Bài viết đã có phản hồi.";
                    return RedirectToAction(nameof(Details), new { id, page });
                case WritingHomeworkWriteStatus.Closed:
                    TempData["Error"] = "Khóa học đã lưu trữ và chỉ có thể xem bài nộp.";
                    return RedirectToAction(nameof(Details), new { id, page });
                case WritingHomeworkWriteStatus.Forbidden:
                    return Forbid();
                case WritingHomeworkWriteStatus.NotFound:
                    return NotFound();
            }
        }

        var detail = await homework.GetAssignmentAsync(UserId, id, page, ct);
        if (detail.Value is null) return Denied(detail.Access);
        if (!detail.Value.Submissions.Any(row => row.Id == submissionId)) return NotFound();

        ViewData["RejectedSubmissionId"] = submissionId;
        ViewData["AttemptedReview"] = input;
        return View(nameof(Details), detail.Value);
    }

    private IActionResult AuthoringView(WritingHomeworkCourseInfo course, WritingAssignmentInput input, WritingAssignmentPlan? preview = null)
    {
        ViewData["Course"] = course;
        ViewData["PreviewPlan"] = preview;
        return View(nameof(Create), input);
    }

    private IActionResult EditView(WritingHomeworkDetail assignment, WritingAssignmentInput input, WritingAssignmentPlan? preview = null)
    {
        ViewData["EditAssignmentId"] = assignment.Id;
        return AuthoringView(new WritingHomeworkCourseInfo(assignment.CourseId, assignment.CourseTitle, true), input, preview);
    }

    private WritingAssignmentPlan ReadPlan(WritingAssignmentInput input)
    {
        var now = time.GetUtcNow();
        var due = ReadLocalDate(input.DueAt, nameof(input.DueAt), "Hạn nộp");
        if (due <= now)
            ModelState.AddModelError(nameof(input.DueAt), "Hạn nộp phải ở trong tương lai.");

        DateTimeOffset? publish = null;
        if (input.PublishMode == WritingAssignmentPublishMode.Scheduled)
        {
            publish = ReadLocalDate(input.PublishAt, nameof(input.PublishAt), "Thời điểm giao");
            if (input.PublishAt is null)
                ModelState.AddModelError(nameof(input.PublishAt), "Chọn thời điểm giao bài.");
            else if (publish <= now)
                ModelState.AddModelError(nameof(input.PublishAt), "Thời điểm giao phải ở trong tương lai.");
            if (due <= publish && due > now)
                ModelState.AddModelError(nameof(input.DueAt), "Hạn nộp phải sau thời điểm giao bài.");
        }

        return new(input.Title, input.Prompt, due, input.PublishMode == WritingAssignmentPublishMode.Draft, publish);
    }

    private DateTimeOffset? ReadLocalDate(DateTime? date, string field, string label)
    {
        if (date is not { } value) return null;
        if (value.Year is >= 2000 and <= 9998)
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), WritingHomeworkLimits.DisplayOffset);
        ModelState.AddModelError(field, $"{label} không hợp lệ.");
        return null;
    }

    private IActionResult PlanSaved(Guid? id, WritingAssignmentPublishMode mode)
    {
        TempData["Success"] = mode switch
        {
            WritingAssignmentPublishMode.Draft => "Đã lưu bản nháp đề bài.",
            WritingAssignmentPublishMode.Scheduled => "Đã hẹn giờ giao bài.",
            _ => "Đã giao bài viết."
        };
        return RedirectToAction(nameof(Details), new { id });
    }

    private IActionResult ReadOnlyAssignment(Guid id)
    {
        TempData["Error"] = "Đề bài hiện chỉ có thể xem.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<IActionResult> ResponseError(Guid id, string? response, string error, CancellationToken ct,
        bool draftChanged = false)
    {
        var detail = await homework.GetAssignmentAsync(UserId, id, 1, ct);
        if (detail.Value is null) return Denied(detail.Access);
        ViewData["SubmissionError"] = error;
        ViewData["AttemptedResponse"] = response ?? "";
        ViewData["DraftChanged"] = draftChanged;
        if (draftChanged) Response.StatusCode = StatusCodes.Status409Conflict;
        return View(nameof(Details), detail.Value);
    }

    private IActionResult RedirectClosed(Guid id)
    {
        TempData["Error"] = "Khóa học đã lưu trữ và đã dừng giao bài.";
        return RedirectToAction(nameof(Course), new { id });
    }

    private IActionResult Denied(LearningAccessResult access) =>
        access == LearningAccessResult.NotFound ? NotFound() : Forbid();
}
