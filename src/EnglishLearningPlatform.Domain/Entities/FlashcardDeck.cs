using EnglishLearningPlatform.Domain.Authorization;
using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class FlashcardDeck : BaseEntity, IOwnedResource
{
    public Guid OwnerTeacherUserId { get; set; }
    Guid IOwnedResource.OwnerUserId => OwnerTeacherUserId;
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public Guid? LessonId { get; set; }
    public Lesson? Lesson { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public FlashcardDeckStatus Status { get; set; } = FlashcardDeckStatus.Draft;
    public ICollection<Flashcard> Cards { get; set; } = new List<Flashcard>();
}
