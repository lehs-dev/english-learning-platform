using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Flashcards;

public sealed record FlashcardDeckSummary(Guid Id, Guid CourseId, string CourseTitle, Guid? LessonId,
    string Title, string? Description, FlashcardDeckStatus Status, int CardCount, int DueCount);
public sealed record FlashcardSheet(Guid Id, string FrontText, string BackText, string? Example,
    string? Pronunciation, string? AudioResourceUrl);
public sealed record FlashcardStudySheet(Guid DeckId, string Title, int TotalCards, int DueCount,
    FlashcardSheet? Card, Guid ReviewRequestId, DateTimeOffset? NextDueAtUtc);
public enum FlashcardWriteStatus { Saved, Forbidden, NotFound, Invalid, Unavailable, Conflict }
public sealed record FlashcardWriteResult(FlashcardWriteStatus Status, Guid? Id = null, string? Error = null);
public sealed record FlashcardReviewResult(FlashcardWriteStatus Status, DateTimeOffset? NextDueAtUtc = null,
    bool IsReplay = false);
public sealed record FlashcardCourseOption(Guid Id, string Title, IReadOnlyList<FlashcardLessonOption> Lessons);
public sealed record FlashcardLessonOption(Guid Id, string Title);
public sealed record TeacherFlashcardDeck(Guid Id, Guid CourseId, Guid? LessonId, string Title,
    string? Description, FlashcardDeckStatus Status, IReadOnlyList<TeacherFlashcard> Cards);
public sealed record TeacherFlashcard(Guid Id, string FrontText, string BackText, string? Example,
    string? Pronunciation, string? AudioResourceUrl, int OrderIndex);
public sealed record SaveFlashcardDeckRequest(Guid CourseId, Guid? LessonId, string Title, string? Description);
public sealed record SaveFlashcardRequest(string FrontText, string BackText, string? Example = null,
    string? Pronunciation = null, string? AudioResourceUrl = null);

public interface IFlashcardService
{
    Task<LearningOutcome<IReadOnlyList<FlashcardDeckSummary>>> GetDecksAsync(Guid studentId, CancellationToken ct = default);
    Task<LearningOutcome<FlashcardStudySheet>> GetStudyAsync(Guid studentId, Guid deckId, CancellationToken ct = default);
    Task<FlashcardReviewResult> ReviewAsync(Guid studentId, Guid deckId, Guid flashcardId,
        Guid reviewRequestId, FlashcardRating rating, CancellationToken ct = default);
}

public interface ITeacherFlashcardService
{
    Task<LearningOutcome<IReadOnlyList<FlashcardDeckSummary>>> GetDecksAsync(Guid teacherId, CancellationToken ct = default);
    Task<LearningOutcome<IReadOnlyList<FlashcardCourseOption>>> GetCoursesAsync(Guid teacherId, CancellationToken ct = default);
    Task<LearningOutcome<TeacherFlashcardDeck>> GetDeckAsync(Guid teacherId, Guid deckId, CancellationToken ct = default);
    Task<FlashcardWriteResult> CreateAsync(Guid teacherId, SaveFlashcardDeckRequest request, CancellationToken ct = default);
    Task<FlashcardWriteResult> UpdateAsync(Guid teacherId, Guid deckId, SaveFlashcardDeckRequest request, CancellationToken ct = default);
    Task<FlashcardWriteResult> SaveCardAsync(Guid teacherId, Guid deckId, Guid? cardId, SaveFlashcardRequest request, CancellationToken ct = default);
    Task<FlashcardWriteResult> DeleteCardAsync(Guid teacherId, Guid deckId, Guid cardId, CancellationToken ct = default);
    Task<FlashcardWriteResult> PublishAsync(Guid teacherId, Guid deckId, CancellationToken ct = default);
    Task<FlashcardWriteResult> ArchiveAsync(Guid teacherId, Guid deckId, CancellationToken ct = default);
}
