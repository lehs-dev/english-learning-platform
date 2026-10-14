using System.Security.Claims;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Web.Models.Assessment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Student)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssessmentController(IAssessmentQueryService queries, IAttemptService attempts,
    IStudentLearningService learning) : Controller
{
    private Guid StudentId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var result = await learning.AssessmentsAsync(StudentId, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Forbid();
    }

    [HttpGet]
    public async Task<IActionResult> Take(Guid id, Guid? attemptId, CancellationToken ct)
    {
        // GET only reads persisted state. Opening/refreshing this page never consumes an attempt.
        var overview = await queries.GetAssessmentOverviewAsync(StudentId, id, ct);
        var selectedAttempt = attemptId ?? overview?.ActiveAttemptId;
        AssessmentTakingSheet? sheet = null;
        if (selectedAttempt is Guid selected)
        {
            sheet = await queries.GetTakingSheetAsync(StudentId, selected, ct);
            if (sheet is not null && sheet.AssessmentId != id) return NotFound();
            if (sheet is null)
            {
                var result = await queries.GetAttemptResultAsync(StudentId, selected, ct);
                if (result is not null)
                    return result.AssessmentId == id ? RedirectToAction(nameof(Result), new { id = selected }) : NotFound();
                if (overview?.AttemptHistory.All(a => a.AttemptId != selected) != false) return NotFound();
            }
        }
        if (overview is null && sheet is null) return NotFound();
        return View(new AssessmentTakePage(overview, sheet));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        if (!ModelState.IsValid || id == Guid.Empty) return BadRequest();
        var result = await attempts.StartAsync(StudentId, id, ct);
        return result.Status switch
        {
            AttemptStartStatus.Started or AttemptStartStatus.Resumed =>
                RedirectToAction(nameof(Take), new { id, attemptId = result.AttemptId }),
            AttemptStartStatus.ExpiredFinalized => RedirectToAction(nameof(Result), new { id = result.AttemptId }),
            AttemptStartStatus.NeedsFinalization when result.AttemptId.HasValue => await Finalize(result.AttemptId.Value, ct),
            AttemptStartStatus.Forbidden => Forbid(),
            AttemptStartStatus.LimitReached => LimitReached(id),
            _ => NotFound()
        };
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Save(Guid id, AssessmentSubmissionInput input, CancellationToken ct) =>
        SaveOrSubmit(id, input, false, ct);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Submit(Guid id, AssessmentSubmissionInput input, CancellationToken ct) =>
        SaveOrSubmit(id, input, true, ct);

    [HttpGet]
    public async Task<IActionResult> Result(Guid id, CancellationToken ct)
    {
        var result = await queries.GetAttemptResultAsync(StudentId, id, ct);
        return result is null ? NotFound() : View(result);
    }

    private IActionResult LimitReached(Guid assessmentId)
    {
        TempData["AssessmentError"] = "Bạn đã sử dụng hết lượt làm bài. Các kết quả đã nộp vẫn được lưu trong lịch sử.";
        return RedirectToAction(nameof(Take), new { id = assessmentId });
    }

    private async Task<IActionResult> SaveOrSubmit(Guid attemptId, AssessmentSubmissionInput input, bool submit, CancellationToken ct)
    {
        if (!ModelState.IsValid || attemptId == Guid.Empty || input.Answers is null) return BadRequest();
        var sheet = await queries.GetTakingSheetAsync(StudentId, attemptId, ct);
        if (sheet is null)
        {
            // Deadline is enforced by the write service; late input cannot replace saved answers.
            // Finalize also validates ownership and preserves an already graded result on replay.
            return await Finalize(attemptId, ct);
        }

        // Validate the entire posted sheet before any write, including duplicate/foreign questions.
        if (input.Answers.Count != sheet.Questions.Count ||
            input.Answers.Select(a => a.AssessmentQuestionId).Distinct().Count() != input.Answers.Count ||
            input.Answers.Any(answer => !sheet.Questions.Any(question =>
                question.AssessmentQuestionId == answer.AssessmentQuestionId &&
                (!answer.SelectedOptionId.HasValue || question.Options.Any(option => option.Id == answer.SelectedOptionId)))))
            return BadRequest();

        foreach (var answer in input.Answers)
        {
            var status = await attempts.SaveAnswerAsync(StudentId, attemptId,
                answer.AssessmentQuestionId, answer.SelectedOptionId, ct);
            switch (status)
            {
                case AttemptSaveStatus.Saved:
                case AttemptSaveStatus.Cleared:
                    break;
                case AttemptSaveStatus.AlreadyFinalized:
                case AttemptSaveStatus.Expired:
                    return RedirectToAction(nameof(Result), new { id = attemptId });
                case AttemptSaveStatus.InvalidAnswer:
                    return BadRequest();
                case AttemptSaveStatus.Forbidden:
                    return Forbid();
                default:
                    return NotFound();
            }
        }

        if (submit) return await Finalize(attemptId, ct);
        if (Request.Headers.Accept.Any(value => value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true))
            return Json(new { saved = true });
        TempData["AssessmentSuccess"] = "Đã lưu đáp án. Bạn có thể tiếp tục lượt làm này trong thời gian còn lại.";
        return RedirectToAction(nameof(Take), new { id = sheet.AssessmentId, attemptId });
    }

    private async Task<IActionResult> Finalize(Guid attemptId, CancellationToken ct)
    {
        var result = await attempts.FinalizeAsync(StudentId, attemptId, ct);
        return result.Status switch
        {
            AttemptFinalizeStatus.Graded or AttemptFinalizeStatus.AlreadyGraded => RedirectToAction(nameof(Result), new { id = attemptId }),
            AttemptFinalizeStatus.Forbidden => Forbid(),
            _ => NotFound()
        };
    }
}
