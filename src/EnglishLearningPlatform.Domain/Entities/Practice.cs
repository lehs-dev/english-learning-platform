using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class Practice : BaseEntity
{
    public Guid OwnerTeacherUserId { get; set; }
    public Guid? LessonId { get; set; }
    public Lesson? Lesson { get; set; }
    public Guid? ModuleId { get; set; }
    public Module? Module { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PracticeStatus Status { get; set; } = PracticeStatus.Draft;

    public ICollection<PracticeQuestion> Questions { get; set; } = new List<PracticeQuestion>();
    public ICollection<PracticeSubmission> Submissions { get; set; } = new List<PracticeSubmission>();
}
