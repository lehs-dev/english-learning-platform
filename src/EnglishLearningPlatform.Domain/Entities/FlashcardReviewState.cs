using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class FlashcardReviewState : BaseEntity
{
    public Guid StudentUserId { get; set; }
    public Guid FlashcardId { get; set; }
    public Flashcard Flashcard { get; set; } = null!;
    public DateTimeOffset LastReviewedAtUtc { get; set; }
    public DateTimeOffset NextDueAtUtc { get; set; }
    public int Repetitions { get; set; }
    public FlashcardRating LastRating { get; set; }
}
