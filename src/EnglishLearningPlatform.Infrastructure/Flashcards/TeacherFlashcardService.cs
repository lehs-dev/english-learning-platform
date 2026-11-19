using System.Data;
using EnglishLearningPlatform.Application.Flashcards;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Flashcards;

public sealed class TeacherFlashcardService(AppDbContext db, IActiveUserValidator users, TimeProvider time) : ITeacherFlashcardService
{
    public async Task<LearningOutcome<IReadOnlyList<FlashcardDeckSummary>>> GetDecksAsync(Guid teacherId, CancellationToken ct = default)
    {
        if (!await users.IsActiveTeacherAsync(teacherId, ct)) return new(LearningAccessResult.Forbidden);
        var decks = await OwnedDecks(teacherId).OrderByDescending(d => d.CreatedAtUtc).ThenBy(d => d.Id)
            .Select(d => new FlashcardDeckSummary(d.Id, d.CourseId, d.Course.Title, d.LessonId, d.Title, d.Description, d.Status, d.Cards.Count, 0))
            .ToListAsync(ct);
        return new(LearningAccessResult.Allowed, decks);
    }

    public async Task<LearningOutcome<IReadOnlyList<FlashcardCourseOption>>> GetCoursesAsync(Guid teacherId, CancellationToken ct = default)
    {
        if (!await users.IsActiveTeacherAsync(teacherId, ct)) return new(LearningAccessResult.Forbidden);
        var courses = await db.Courses.AsNoTracking().Where(c => c.OwnerTeacherUserId == teacherId && c.Status != CourseStatus.Archived)
            .OrderBy(c => c.Title).Select(c => new FlashcardCourseOption(c.Id, c.Title,
                c.Modules.OrderBy(m => m.OrderIndex).ThenBy(m => m.Id).SelectMany(m => m.Lessons.OrderBy(l => l.OrderIndex).ThenBy(l => l.Id))
                    .Select(l => new FlashcardLessonOption(l.Id, l.Module.Title + " / " + l.Title)).ToList())).ToListAsync(ct);
        return new(LearningAccessResult.Allowed, courses);
    }

    public async Task<LearningOutcome<TeacherFlashcardDeck>> GetDeckAsync(Guid teacherId, Guid deckId, CancellationToken ct = default)
    {
        if (!await users.IsActiveTeacherAsync(teacherId, ct)) return new(LearningAccessResult.Forbidden);
        var deck = await OwnedDecks(teacherId).Where(d => d.Id == deckId).Select(d => new TeacherFlashcardDeck(d.Id, d.CourseId,
            d.LessonId, d.Title, d.Description, d.Status, d.Cards.OrderBy(c => c.OrderIndex).ThenBy(c => c.Id)
                .Select(c => new TeacherFlashcard(c.Id, c.FrontText, c.BackText, c.Example, c.Pronunciation, c.AudioResourceUrl, c.OrderIndex)).ToList()))
            .SingleOrDefaultAsync(ct);
        return deck == null ? new(LearningAccessResult.NotFound) : new(LearningAccessResult.Allowed, deck);
    }

    public async Task<FlashcardWriteResult> CreateAsync(Guid teacherId, SaveFlashcardDeckRequest request, CancellationToken ct = default)
    {
        if (!await users.IsActiveTeacherAsync(teacherId, ct)) return new(FlashcardWriteStatus.Forbidden);
        if (!ValidDeck(request)) return Invalid("Tên bộ thẻ cần 1–200 ký tự; mô tả tối đa 4.000 ký tự.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var course = await db.CoursesForUpdate(request.CourseId, teacherId).SingleOrDefaultAsync(ct);
        if (course == null) return new(FlashcardWriteStatus.NotFound);
        if (course.Status == CourseStatus.Archived) return new(FlashcardWriteStatus.Unavailable);
        if (!await ValidLessonAsync(request.CourseId, request.LessonId, ct)) return Invalid("Bài học phải thuộc khóa học đã chọn.");
        if (!await users.IsActiveTeacherAsync(teacherId, ct)) return new(FlashcardWriteStatus.Forbidden);
        var deck = new FlashcardDeck { OwnerTeacherUserId = teacherId, CourseId = course.Id, LessonId = request.LessonId,
            Title = request.Title.Trim(), Description = request.Description?.Trim() };
        db.Set<FlashcardDeck>().Add(deck);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(FlashcardWriteStatus.Saved, deck.Id);
    }

    public async Task<FlashcardWriteResult> UpdateAsync(Guid teacherId, Guid deckId, SaveFlashcardDeckRequest request, CancellationToken ct = default)
    {
        if (!await users.IsActiveTeacherAsync(teacherId, ct)) return new(FlashcardWriteStatus.Forbidden);
        if (!ValidDeck(request)) return Invalid("Tên bộ thẻ cần 1–200 ký tự; mô tả tối đa 4.000 ký tự.");
        var courseId = await OwnedDecks(teacherId).Where(d => d.Id == deckId).Select(d => (Guid?)d.CourseId).SingleOrDefaultAsync(ct);
        if (!courseId.HasValue) return new(FlashcardWriteStatus.NotFound);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var deck = await LockDeckAsync(teacherId, deckId, courseId.Value, ct);
        if (deck == null) return new(FlashcardWriteStatus.NotFound);
        if (deck.Status != FlashcardDeckStatus.Draft) return Invalid("Chỉ có thể sửa bộ thẻ ở trạng thái nháp.");
        if (request.CourseId != deck.CourseId || !await ValidLessonAsync(deck.CourseId, request.LessonId, ct))
            return Invalid("Bài học phải thuộc khóa học của bộ thẻ.");
        deck.LessonId = request.LessonId;
        deck.Title = request.Title.Trim();
        deck.Description = request.Description?.Trim();
        deck.UpdatedAtUtc = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(FlashcardWriteStatus.Saved, deck.Id);
    }

    public async Task<FlashcardWriteResult> SaveCardAsync(Guid teacherId, Guid deckId, Guid? cardId, SaveFlashcardRequest request, CancellationToken ct = default)
    {
        if (!await users.IsActiveTeacherAsync(teacherId, ct)) return new(FlashcardWriteStatus.Forbidden);
        if (!ValidCard(request)) return Invalid("Kiểm tra mặt trước (500), mặt sau (1.000), ví dụ (2.000), phiên âm (200) và đường dẫn âm thanh HTTPS (2.000 ký tự).");
        var courseId = await OwnedDecks(teacherId).Where(d => d.Id == deckId).Select(d => (Guid?)d.CourseId).SingleOrDefaultAsync(ct);
        if (!courseId.HasValue) return new(FlashcardWriteStatus.NotFound);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var deck = await LockDeckAsync(teacherId, deckId, courseId.Value, ct);
        if (deck == null) return new(FlashcardWriteStatus.NotFound);
        if (deck.Status != FlashcardDeckStatus.Draft) return Invalid("Chỉ có thể sửa thẻ trong bộ thẻ nháp.");
        Flashcard? card;
        if (cardId.HasValue)
        {
            card = await db.Set<Flashcard>().SingleOrDefaultAsync(c => c.Id == cardId && c.DeckId == deckId, ct);
            if (card == null) return new(FlashcardWriteStatus.NotFound);
        }
        else
        {
            if (await db.Set<Flashcard>().CountAsync(c => c.DeckId == deckId, ct) >= 1000) return Invalid("Mỗi bộ thẻ có tối đa 1.000 thẻ.");
            int next = (await db.Set<Flashcard>().Where(c => c.DeckId == deckId).MaxAsync(c => (int?)c.OrderIndex, ct) ?? -1) + 1;
            card = new Flashcard { DeckId = deckId, OrderIndex = next };
            db.Set<Flashcard>().Add(card);
        }
        card.FrontText = request.FrontText.Trim();
        card.BackText = request.BackText.Trim();
        card.Example = request.Example?.Trim();
        card.Pronunciation = request.Pronunciation?.Trim();
        card.AudioResourceUrl = string.IsNullOrWhiteSpace(request.AudioResourceUrl) ? null : request.AudioResourceUrl.Trim();
        card.UpdatedAtUtc = deck.UpdatedAtUtc = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(FlashcardWriteStatus.Saved, card.Id);
    }

    public async Task<FlashcardWriteResult> DeleteCardAsync(Guid teacherId, Guid deckId, Guid cardId, CancellationToken ct = default)
    {
        if (!await users.IsActiveTeacherAsync(teacherId, ct)) return new(FlashcardWriteStatus.Forbidden);
        var courseId = await OwnedDecks(teacherId).Where(d => d.Id == deckId).Select(d => (Guid?)d.CourseId).SingleOrDefaultAsync(ct);
        if (!courseId.HasValue) return new(FlashcardWriteStatus.NotFound);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var deck = await LockDeckAsync(teacherId, deckId, courseId.Value, ct);
        if (deck == null) return new(FlashcardWriteStatus.NotFound);
        if (deck.Status != FlashcardDeckStatus.Draft) return Invalid("Chỉ có thể xóa thẻ trong bộ thẻ nháp.");
        var card = await db.Set<Flashcard>().SingleOrDefaultAsync(c => c.Id == cardId && c.DeckId == deckId, ct);
        if (card == null) return new(FlashcardWriteStatus.NotFound);
        if (await db.Set<FlashcardReview>().AnyAsync(r => r.FlashcardId == cardId, ct)) return Invalid("Thẻ đã được ôn tập không thể xóa.");
        db.Set<Flashcard>().Remove(card);
        deck.UpdatedAtUtc = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(FlashcardWriteStatus.Saved, deckId);
    }

    public async Task<FlashcardWriteResult> PublishAsync(Guid teacherId, Guid deckId, CancellationToken ct = default)
    {
        if (!await users.IsActiveTeacherAsync(teacherId, ct)) return new(FlashcardWriteStatus.Forbidden);
        var courseId = await OwnedDecks(teacherId).Where(d => d.Id == deckId).Select(d => (Guid?)d.CourseId).SingleOrDefaultAsync(ct);
        if (!courseId.HasValue) return new(FlashcardWriteStatus.NotFound);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var deck = await LockDeckAsync(teacherId, deckId, courseId.Value, ct);
        if (deck == null) return new(FlashcardWriteStatus.NotFound);
        if (deck.Status == FlashcardDeckStatus.Archived) return Invalid("Bộ thẻ đã lưu trữ không thể xuất bản lại.");
        if (!await db.Set<Flashcard>().AnyAsync(c => c.DeckId == deckId, ct)) return Invalid("Thêm ít nhất một thẻ trước khi xuất bản.");
        if (!await ValidLessonAsync(deck.CourseId, deck.LessonId, ct)) return Invalid("Bài học không còn thuộc khóa học của bộ thẻ.");
        deck.Status = FlashcardDeckStatus.Published;
        deck.UpdatedAtUtc = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(FlashcardWriteStatus.Saved, deckId);
    }

    public async Task<FlashcardWriteResult> ArchiveAsync(Guid teacherId, Guid deckId, CancellationToken ct = default)
    {
        if (!await users.IsActiveTeacherAsync(teacherId, ct)) return new(FlashcardWriteStatus.Forbidden);
        var courseId = await OwnedDecks(teacherId).Where(d => d.Id == deckId).Select(d => (Guid?)d.CourseId).SingleOrDefaultAsync(ct);
        if (!courseId.HasValue) return new(FlashcardWriteStatus.NotFound);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var deck = await LockDeckAsync(teacherId, deckId, courseId.Value, ct);
        if (deck == null) return new(FlashcardWriteStatus.NotFound);
        deck.Status = FlashcardDeckStatus.Archived;
        deck.UpdatedAtUtc = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(FlashcardWriteStatus.Saved, deckId);
    }

    private IQueryable<FlashcardDeck> OwnedDecks(Guid teacherId) => db.Set<FlashcardDeck>().AsNoTracking()
        .Where(d => d.OwnerTeacherUserId == teacherId && d.Course.OwnerTeacherUserId == teacherId);

    private async Task<FlashcardDeck?> LockDeckAsync(Guid teacherId, Guid deckId, Guid courseId, CancellationToken ct)
    {
        // Resolve the routing ID before opening the transaction; a shared deck lock here could invert the course-first order.
        var course = await db.CoursesForUpdate(courseId, teacherId).SingleOrDefaultAsync(ct);
        if (course == null || course.Status == CourseStatus.Archived || !await users.IsActiveTeacherAsync(teacherId, ct)) return null;
        return await db.Set<FlashcardDeck>().FromSqlInterpolated(
            $"SELECT * FROM [FlashcardDecks] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {deckId} AND [OwnerTeacherUserId] = {teacherId} AND [CourseId] = {courseId}")
            .SingleOrDefaultAsync(ct);
    }

    private Task<bool> ValidLessonAsync(Guid courseId, Guid? lessonId, CancellationToken ct) => lessonId.HasValue
        ? db.Lessons.AnyAsync(l => l.Id == lessonId && l.Module.CourseId == courseId, ct) : Task.FromResult(true);
    private static bool ValidDeck(SaveFlashcardDeckRequest? r) => r != null && r.CourseId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(r.Title) && r.Title.Length <= 200 && (r.Description?.Length ?? 0) <= 4000;
    private static bool ValidCard(SaveFlashcardRequest? r) => r != null &&
        !string.IsNullOrWhiteSpace(r.FrontText) && r.FrontText.Length <= 500 && !string.IsNullOrWhiteSpace(r.BackText) && r.BackText.Length <= 1000 &&
        (r.Example?.Length ?? 0) <= 2000 && (r.Pronunciation?.Length ?? 0) <= 200 && (r.AudioResourceUrl?.Length ?? 0) <= 2000 &&
        (string.IsNullOrWhiteSpace(r.AudioResourceUrl) || (Uri.TryCreate(r.AudioResourceUrl.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps));
    private static FlashcardWriteResult Invalid(string error) => new(FlashcardWriteStatus.Invalid, Error: error);
}
