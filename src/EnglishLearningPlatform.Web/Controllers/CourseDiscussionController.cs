using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Web.Models.Courses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Roles = AppRoles.Student + "," + AppRoles.Teacher, Policy = AuthorizationPolicies.SingleActiveRole)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CourseDiscussionController(ICourseDiscussionService discussion) : Controller
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public Task<IActionResult> Index(Guid id, CancellationToken ct, int page = 1) =>
        InboxAsync(id, page, Guid.NewGuid(), null, null, ct);

    [HttpGet]
    public Task<IActionResult> Details(Guid id, CancellationToken ct, int page = 1) =>
        ConversationAsync(id, page, Guid.NewGuid(), null, ct);

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(128 * 1024), Authorize(Policy = AuthorizationPolicies.Student)]
    public async Task<IActionResult> Ask([FromRoute] Guid id, [FromForm] Guid requestId, [FromForm] string? subject,
        [FromForm] string? body, CancellationToken ct)
    {
        var result = await discussion.AskAsync(UserId, id, requestId, subject, body, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        if (!result.Success)
        {
            AddErrors(result);
            return await InboxAsync(id, 1, requestId, subject, body, ct);
        }

        TempData["Success"] = "Đã gửi câu hỏi đến giảng viên.";
        return RedirectToAction(nameof(Details), new { id = result.Id });
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(128 * 1024)]
    public async Task<IActionResult> Reply([FromRoute] Guid id, [FromForm] Guid requestId, [FromForm] string? body,
        CancellationToken ct)
    {
        var result = await discussion.ReplyAsync(UserId, id, requestId, body, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        if (!result.Success)
        {
            AddErrors(result);
            return await ConversationAsync(id, 1, requestId, body, ct);
        }

        TempData["Success"] = "Đã gửi phản hồi.";
        return RedirectToAction(nameof(Details), null, new { id }, "discussion-reply");
    }

    private async Task<IActionResult> InboxAsync(Guid courseId, int page, Guid requestId, string? subject, string? body,
        CancellationToken ct)
    {
        var result = await discussion.GetInboxAsync(UserId, courseId, page, ct);
        return result.Access == LearningAccessResult.Allowed
            ? View(nameof(Index), new CourseDiscussionInboxViewModel(result.Value!, FormId(requestId), subject, body))
            : Denied(result.Access);
    }

    private async Task<IActionResult> ConversationAsync(Guid threadId, int page, Guid requestId, string? body,
        CancellationToken ct)
    {
        var result = await discussion.GetConversationAsync(UserId, threadId, page, ct);
        return result.Access == LearningAccessResult.Allowed
            ? View(nameof(Details), new CourseDiscussionConversationViewModel(result.Value!, FormId(requestId), body))
            : Denied(result.Access);
    }

    private static Guid FormId(Guid requestId) => requestId == Guid.Empty ? Guid.NewGuid() : requestId;

    private IActionResult Denied(LearningAccessResult access) => access == LearningAccessResult.NotFound ? NotFound() : Forbid();

    private void AddErrors(WriteOutcome result)
    {
        foreach (var (field, messages) in result.Errors)
        foreach (var message in messages)
            ModelState.TryAddModelError(field, message);
    }
}
