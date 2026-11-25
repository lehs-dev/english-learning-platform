using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class CourseDiscussionThread : BaseEntity
{
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public string Subject { get; set; } = string.Empty;
    public ICollection<CourseDiscussionMessage> Messages { get; set; } = [];
}
