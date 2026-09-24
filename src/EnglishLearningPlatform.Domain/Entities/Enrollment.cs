using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class Enrollment : BaseEntity
{
    public Guid StudentUserId { get; set; }
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public Guid? LastAccessedLessonId { get; set; }
    public Lesson? LastAccessedLesson { get; set; }
    public DateTimeOffset EnrolledAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public ICollection<LessonProgress> LessonProgressEntries { get; set; } = new List<LessonProgress>();
}
