using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class LearningProgress : BaseEntity
{
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;
    public Guid StudentUserId { get; set; }
    public ProgressStatus Status { get; set; } = ProgressStatus.NotStarted;
    public DateTimeOffset? LastAccessedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
