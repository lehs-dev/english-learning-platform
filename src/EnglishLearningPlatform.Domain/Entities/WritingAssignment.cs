using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class WritingAssignment : BaseEntity
{
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public DateTimeOffset? DueAtUtc { get; set; }
    public ICollection<WritingSubmission> Submissions { get; set; } = [];
}
