using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class WritingSubmission : BaseEntity
{
    public Guid AssignmentId { get; set; }
    public WritingAssignment Assignment { get; set; } = null!;
    public Guid StudentUserId { get; set; }
    public string Response { get; set; } = string.Empty;
    public string? Feedback { get; set; }
    public int? Score { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
}
