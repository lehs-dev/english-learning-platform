using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Web.Models.Practice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Student)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PracticeController(IPracticeService practices, IStudentLearningService learning) : Controller
{
    private Guid StudentId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private IActionResult Denied(LearningAccessResult access) => access == LearningAccessResult.Forbidden ? Forbid() : NotFound();

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var result = await learning.PracticeHubAsync(StudentId, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Denied(result.Access);
    }

    [HttpGet]
    public async Task<IActionResult> Take(Guid id, CancellationToken ct)
    {
        var result = await practices.BeginAsync(StudentId, id, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Denied(result.Access);
    }

    [HttpGet]
    public async Task<IActionResult> Review(Guid id, CancellationToken ct)
    {
        var result = await practices.GetMistakeSheetAsync(StudentId, id, ct);
        return result.Access == LearningAccessResult.Allowed ? View(nameof(Take), result.Value) : Denied(result.Access);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid id, PracticeSubmissionInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid || id == Guid.Empty || input.Answers is null || input.ReviewStamp is not null) return BadRequest();
        var answers = Responses(input.Answers);
        // Phiên do server cấp, không nhận version tùy ý từ browser.
        var result = input.SessionId.HasValue
            ? await practices.SubmitSessionAsync(StudentId, id, input.SessionId.Value, answers, ct)
            : await practices.SubmitAsync(StudentId, id, answers, ct);
        return result.Status switch
        {
            PracticeSubmitStatus.Saved => RedirectToAction(nameof(Result), new { id }),
            PracticeSubmitStatus.Forbidden => Forbid(),
            PracticeSubmitStatus.InvalidAnswers => BadRequest(),
            PracticeSubmitStatus.Unavailable => View("Unavailable"),
            _ => NotFound()
        };
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckReview(Guid id, PracticeSubmissionInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid || id == Guid.Empty || input.Answers is null || input.ReviewStamp is null || input.SessionId.HasValue)
            return BadRequest();

        var result = await practices.CheckMistakesAsync(StudentId, id, input.ReviewStamp, Responses(input.Answers), ct);
        switch (result.Status)
        {
            case PracticeReviewStatus.Graded:
                ViewData["MistakeReview"] = true;
                return View(nameof(Result), result.Feedback);
            case PracticeReviewStatus.Forbidden:
                return Forbid();
            case PracticeReviewStatus.InvalidAnswers:
                return BadRequest();
            default:
                Response.StatusCode = StatusCodes.Status409Conflict;
                ViewData["StaleReviewPracticeId"] = id;
                return View("Unavailable");
        }
    }

    [HttpGet]
    public async Task<IActionResult> Result(Guid id, CancellationToken ct, bool mistakesOnly = false)
    {
        var result = await practices.GetResultAsync(StudentId, id, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        ViewData["MistakesOnly"] = mistakesOnly;
        var current = await practices.GetSheetAsync(StudentId, id, ct);
        ViewData["CanRetry"] = current.Access == LearningAccessResult.Allowed;
        ViewData["CanReviewMistakes"] = current.Value?.Version == result.Value?.Version &&
            result.Value!.Questions.Any(question => !question.IsCorrect);
        return View(result.Value);
    }

    private static List<PracticeResponse> Responses(List<PracticeAnswerInput> answers) =>
        answers.Select(answer => new PracticeResponse(answer.PracticeQuestionId, answer.SelectedOptionId,
            answer.TextAnswer, answer.StructuredAnswerJson)).ToList();
}
