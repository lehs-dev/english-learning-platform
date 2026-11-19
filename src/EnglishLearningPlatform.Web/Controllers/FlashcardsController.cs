using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Flashcards;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Student)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FlashcardsController(IFlashcardService flashcards) : Controller
{
    private Guid StudentId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var result = await flashcards.GetDecksAsync(StudentId, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Forbid();
    }
    [HttpGet]
    public async Task<IActionResult> Study(Guid id, CancellationToken ct)
    {
        var result = await flashcards.GetStudyAsync(StudentId, id, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value)
            : result.Access == LearningAccessResult.Forbidden ? Forbid() : NotFound();
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(Guid id, Guid flashcardId, Guid reviewRequestId, FlashcardRating rating, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest();
        var result = await flashcards.ReviewAsync(StudentId, id, flashcardId, reviewRequestId, rating, ct);
        switch (result.Status)
        {
            case FlashcardWriteStatus.Saved: return RedirectToAction(nameof(Study), new { id });
            case FlashcardWriteStatus.Forbidden: return Forbid();
            case FlashcardWriteStatus.NotFound: return NotFound();
            case FlashcardWriteStatus.Invalid: return BadRequest();
            case FlashcardWriteStatus.Conflict:
                TempData["Success"] = "Thẻ đã được ghi nhận. Hãy tiếp tục với thẻ đến hạn tiếp theo.";
                return RedirectToAction(nameof(Study), new { id });
            default:
                TempData["Error"] = "Bộ thẻ hiện không khả dụng.";
                return RedirectToAction(nameof(Index));
        }
    }
}
