using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class FlashcardReview : BaseEntity
{
    public Guid StudentUserId { get; set; }
    public Guid FlashcardId { get; set; }
    public Flashcard Flashcard { get; set; } = null!;
    public Guid ReviewRequestId { get; set; }
    public FlashcardRating Rating { get; set; }
    public DateTimeOffset ReviewedAtUtc { get; set; }
}
