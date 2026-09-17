using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class ExamAttempt : BaseEntity
{
    public Guid ExamId { get; set; }
    public Exam Exam { get; set; } = null!;
    public Guid StudentUserId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }

    public ICollection<StudentAnswer> StudentAnswers { get; set; } = new List<StudentAnswer>();
}
