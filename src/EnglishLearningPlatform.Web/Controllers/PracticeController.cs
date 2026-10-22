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
        var result = await practices.GetSheetAsync(StudentId, id, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Denied(result.Access);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid id, PracticeSubmissionInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid || id == Guid.Empty || input.Answers is null) return BadRequest();
        var result = await practices.SubmitAsync(StudentId, id, input.Answers
            .Select(a => new PracticeResponse(a.PracticeQuestionId, a.SelectedOptionId)).ToList(), ct);
        return result.Status switch
        {
            PracticeSubmitStatus.Saved => RedirectToAction(nameof(Result), new { id }),
            PracticeSubmitStatus.Forbidden => Forbid(),
            PracticeSubmitStatus.InvalidAnswers => BadRequest(),
            PracticeSubmitStatus.Unavailable => View("Unavailable"),
            _ => NotFound()
        };
    }

    [HttpGet]
    public async Task<IActionResult> Result(Guid id, CancellationToken ct)
    {
        var result = await practices.GetResultAsync(StudentId, id, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        ViewData["CanRetry"] = (await practices.GetSheetAsync(StudentId, id, ct)).Access == LearningAccessResult.Allowed;
        return View(result.Value);
    }
}
