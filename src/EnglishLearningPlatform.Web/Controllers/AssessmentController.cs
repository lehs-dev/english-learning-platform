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
    IStudentLearningService learning, TimeProvider timeProvider) : Controller
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
        // Mở/F5 không Start lượt mới. Lượt đã quá hạn có thể được chấm từ đáp án cũ.
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
            }
        }
        if (sheet is null)
        {
            // Lazy-finalize không phụ thuộc browser chạy JavaScript hoặc gửi Submit trở lại.
            // Chỉ dùng đáp án đã lưu; không nhận đáp án hay deadline từ GET.
            var expiredId = await queries.FindExpiredAttemptIdAsync(StudentId, id, selectedAttempt, ct);
            if (expiredId.HasValue)
            {
                var finalized = await attempts.FinalizeExpiredAsync(StudentId, expiredId.Value, ct);
                if (finalized.Status is AttemptFinalizeStatus.Graded or AttemptFinalizeStatus.AlreadyGraded)
                    return RedirectToAction(nameof(Result), new { id = expiredId.Value });
                if (finalized.Status == AttemptFinalizeStatus.Forbidden) return Forbid();
                // Giữ màn hình fallback nếu chưa chấm được; tuyệt đối không Start lượt khác.
                return View("Expired", new AssessmentExpiredPage(expiredId.Value));
            }

            if (selectedAttempt.HasValue && overview?.AttemptHistory.All(a => a.AttemptId != selectedAttempt.Value) != false)
                return NotFound();
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
        // Người dùng có thể mở link Result khi lượt vừa hết hạn nhưng chưa có Submit.
        // Service tự kiểm tra owner và chỉ chấm InProgress đã đến deadline.
        await attempts.FinalizeExpiredAsync(StudentId, id, ct);
        var result = await queries.GetAttemptResultAsync(StudentId, id, ct);
        return result is null ? NotFound() : View(result);
    }

    [HttpGet]
    public async Task<IActionResult> History(Guid? id, CancellationToken ct)
    {
        // StudentId luôn lấy từ phiên đăng nhập, không nhận userId từ URL/form.
        var history = await queries.GetAttemptHistoryAsync(StudentId, id, ct);
        if (history is null)
            return Forbid();
        if (id.HasValue && history.Count == 0)
            return NotFound();

        bool graded = false;
        foreach (var assessment in history)
        {
            foreach (var attempt in assessment.Attempts)
            {
                if (attempt.Status != AttemptStatus.InProgress || attempt.DeadlineUtc > timeProvider.GetUtcNow())
                    continue;

                var result = await attempts.FinalizeExpiredAsync(StudentId, attempt.AttemptId, ct);
                if (result.Status is AttemptFinalizeStatus.Graded or AttemptFinalizeStatus.AlreadyGraded)
                    graded = true;
            }
        }
        // Query vẫn chỉ đọc. Sau khi service chấm, tải lại để History trả trạng thái/điểm thật.
        if (graded)
        {
            history = await queries.GetAttemptHistoryAsync(StudentId, id, ct);
            if (history is null) return Forbid();
        }

        ViewData["FilteredHistory"] = id.HasValue;
        return View(history);
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
            // Sheet có thể mất vì hết giờ HOẶC mất quyền Enrollment, không đồng nghĩa đã nộp.
            // Chỉ chấm khi server xác nhận đã hết hạn; không để Save từ tab cũ chấm sớm.
            return await Finalize(attemptId, ct, onlyExpired: true);
        }

        // Accept partial answer sets: autosave sends only the question(s) changed in
        // this tab, so answers saved by another tab remain untouched. Still reject
        // duplicate/foreign questions and options that do not belong to the question.
        if (input.Answers.Select(a => a.AssessmentQuestionId).Distinct().Count() != input.Answers.Count ||
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

    private async Task<IActionResult> Finalize(Guid attemptId, CancellationToken ct, bool onlyExpired = false)
    {
        var result = onlyExpired
            ? await attempts.FinalizeExpiredAsync(StudentId, attemptId, ct)
            : await attempts.FinalizeAsync(StudentId, attemptId, ct);
        return result.Status switch
        {
            AttemptFinalizeStatus.Graded or AttemptFinalizeStatus.AlreadyGraded => RedirectToAction(nameof(Result), new { id = attemptId }),
            AttemptFinalizeStatus.Forbidden => Forbid(),
            _ => NotFound()
        };
    }
}
