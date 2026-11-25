using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class CourseDiscussionMessage : BaseEntity
{
    public Guid ThreadId { get; set; }
    public CourseDiscussionThread Thread { get; set; } = null!;
    public Guid AuthorUserId { get; set; }
    public int SequenceNumber { get; set; }
    public bool WrittenByTeacher { get; set; }
    public string Body { get; set; } = string.Empty;
}
