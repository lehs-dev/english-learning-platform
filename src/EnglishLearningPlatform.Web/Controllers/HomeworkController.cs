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
    public async Task<IActionResult> Create(Guid id, WritingAssignmentInput input, CancellationToken ct)
    {
        DateTimeOffset? deadline = null;
        if (input.DueAt is { } local)
        {
            if (local.Year is < 2000 or > 9998)
                ModelState.AddModelError(nameof(input.DueAt), "Hạn nộp không hợp lệ.");
            else
                deadline = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), WritingHomeworkLimits.DisplayOffset);
            if (deadline <= time.GetUtcNow())
                ModelState.AddModelError(nameof(input.DueAt), "Hạn nộp phải ở trong tương lai.");
        }

        if (ModelState.IsValid)
        {
            var result = await homework.AssignAsync(UserId, id, input.Title, input.Prompt, deadline, ct);
            switch (result.Status)
            {
                case WritingHomeworkWriteStatus.Saved:
                    TempData["Success"] = "Đã giao bài viết.";
                    return RedirectToAction(nameof(Details), new { id = result.AssignmentId });
                case WritingHomeworkWriteStatus.Closed:
                    return RedirectClosed(id);
                case WritingHomeworkWriteStatus.Forbidden:
                    return Forbid();
                case WritingHomeworkWriteStatus.NotFound:
                    return NotFound();
                default:
                    ModelState.AddModelError(string.Empty, "Kiểm tra lại tiêu đề, đề bài và hạn nộp.");
                    break;
            }
        }

        var access = await homework.GetAuthoringCourseAsync(UserId, id, ct);
        if (access.Value is not { } course) return Denied(access.Access);
        return course.CanAssign ? AuthoringView(course, input) : RedirectClosed(id);
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, int page, CancellationToken ct)
    {
        var result = await homework.GetAssignmentAsync(UserId, id, page, ct);
        return result.Value is { } assignment ? View(assignment) : Denied(result.Access);
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
                TempData["Error"] = "Bài viết đã hết hạn nộp.";
                break;
            case WritingHomeworkWriteStatus.Invalid:
                var detail = await homework.GetAssignmentAsync(UserId, id, 1, ct);
                if (detail.Value is null) return Denied(detail.Access);
                ViewData["SubmissionError"] = $"Nhập bài viết từ 1 đến {WritingHomeworkLimits.ResponseLength} ký tự.";
                ViewData["AttemptedResponse"] = response;
                return View(nameof(Details), detail.Value);
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

    private IActionResult AuthoringView(WritingHomeworkCourseInfo course, WritingAssignmentInput input)
    {
        ViewData["Course"] = course;
        return View(nameof(Create), input);
    }

    private IActionResult RedirectClosed(Guid id)
    {
        TempData["Error"] = "Khóa học đã lưu trữ và đã dừng giao bài.";
        return RedirectToAction(nameof(Course), new { id });
    }

    private IActionResult Denied(LearningAccessResult access) =>
        access == LearningAccessResult.NotFound ? NotFound() : Forbid();
}
