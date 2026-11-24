using System.Security.Claims;
using System.Text;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Flashcards;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Web.Infrastructure;
using EnglishLearningPlatform.Web.Models.Flashcards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Teacher)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherFlashcardsController(ITeacherFlashcardService flashcards) : Controller
{
    private Guid TeacherId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var result = await flashcards.GetDecksAsync(TeacherId, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Forbid();
    }
    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct, Guid? courseId = null, Guid? lessonId = null)
    {
        var courses = await flashcards.GetCoursesAsync(TeacherId, ct);
        if (courses.Access != LearningAccessResult.Allowed) return Forbid();
        if (!ModelState.IsValid || (lessonId.HasValue && !courseId.HasValue)) return BadRequest();
        var available = courses.Value!;
        if (courseId.HasValue && !available.Any(c => c.Id == courseId &&
            (!lessonId.HasValue || c.Lessons.Any(l => l.Id == lessonId)))) return NotFound();
        return View(new FlashcardDeckForm(new FlashcardDeckInput
        {
            CourseId = courseId.GetValueOrDefault(),
            LessonId = lessonId
        }, available));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] FlashcardDeckInput input, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await flashcards.CreateAsync(TeacherId, input.ToRequest(), ct);
            if (result.Status == FlashcardWriteStatus.Saved) return RedirectToAction(nameof(Edit), new { id = result.Id });
            if (result.Status == FlashcardWriteStatus.Forbidden) return Forbid();
            if (result.Status == FlashcardWriteStatus.NotFound) return NotFound();
            ModelState.AddModelError(string.Empty, result.Error ?? "Không thể tạo bộ thẻ cho khóa học này.");
        }
        var courses = await flashcards.GetCoursesAsync(TeacherId, ct);
        return courses.Access == LearningAccessResult.Allowed ? View(new FlashcardDeckForm(input, courses.Value!)) : Forbid();
    }
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var result = await flashcards.GetDeckAsync(TeacherId, id, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        var courses = await flashcards.GetCoursesAsync(TeacherId, ct);
        return courses.Access == LearningAccessResult.Allowed ? View(new TeacherFlashcardEditModel(result.Value!, courses.Value!)) : Forbid();
    }
    [HttpGet]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct)
    {
        var result = await flashcards.GetDeckAsync(TeacherId, id, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Denied(result.Access);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, FlashcardDeckInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Kiểm tra tên và độ dài các trường của bộ thẻ."; return RedirectToAction(nameof(Edit), new { id }); }
        return WriteResult(await flashcards.UpdateAsync(TeacherId, id, input.ToRequest(), ct), id, "Đã lưu bộ thẻ.");
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCard(Guid id, Guid? cardId, FlashcardCardInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Mặt trước và mặt sau là bắt buộc. Kiểm tra độ dài nội dung thẻ."; return RedirectToAction(nameof(Edit), new { id }); }
        return WriteResult(await flashcards.SaveCardAsync(TeacherId, id, cardId, input.ToRequest(), ct), id, "Đã lưu thẻ.");
    }
    [HttpGet]
    public IActionResult CsvExample() =>
        File(Encoding.UTF8.GetBytes(FlashcardCsv.Example), "text/csv; charset=utf-8", "flashcards.csv");

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(FlashcardCsv.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = FlashcardCsv.MaxRequestBytes)]
    public async Task<IActionResult> ImportCards(Guid id, IFormFile? csvFile, CancellationToken ct)
    {
        if (!ModelState.IsValid || id == Guid.Empty) return BadRequest();
        var (cards, error) = await FlashcardCsv.ReadAsync(csvFile, ct);
        if (cards is null)
            return WriteResult(new(FlashcardWriteStatus.Invalid, Error: error), id, string.Empty);

        var result = await flashcards.ImportCardsAsync(TeacherId, id, cards, ct);
        var success = result.AddedCardCount == 0 ? "Các thẻ trong tệp đã có trong bộ thẻ." : $"Đã nhập {result.AddedCardCount} thẻ.";
        return WriteResult(result, id, success);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCard(Guid id, Guid cardId, CancellationToken ct) =>
        WriteResult(await flashcards.DeleteCardAsync(TeacherId, id, cardId, ct), id, "Đã xóa thẻ.");
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) =>
        WriteResult(await flashcards.PublishAsync(TeacherId, id, ct), id, "Đã xuất bản bộ thẻ.");
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) =>
        WriteResult(await flashcards.ArchiveAsync(TeacherId, id, ct), id, "Đã lưu trữ bộ thẻ.");
    private IActionResult WriteResult(FlashcardWriteResult result, Guid id, string success)
    {
        if (result.Status == FlashcardWriteStatus.Forbidden) return Forbid();
        if (result.Status == FlashcardWriteStatus.NotFound) return NotFound();
        TempData[result.Status == FlashcardWriteStatus.Saved ? "Success" : "Error"] = result.Status == FlashcardWriteStatus.Saved ? success : result.Error ?? "Bộ thẻ hiện không khả dụng.";
        return RedirectToAction(nameof(Edit), new { id });
    }
    private IActionResult Denied(LearningAccessResult access) => access == LearningAccessResult.Forbidden ? Forbid() : NotFound();
}
