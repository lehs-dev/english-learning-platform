using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class Attempt : BaseEntity
{
    public Guid StudentUserId { get; set; }
    public Guid AssessmentId { get; set; }
    public Assessment Assessment { get; set; } = null!;
    public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset DeadlineUtc { get; set; }
    public DateTimeOffset? FinalizedAtUtc { get; set; }
    public AttemptFinalizationReason? FinalizationReason { get; set; }
    public decimal? OverallScore { get; set; }
    public bool? Passed { get; set; }

    public ICollection<AttemptAnswer> Answers { get; set; } = new List<AttemptAnswer>();
    public ICollection<AttemptSkillResult> SkillResults { get; set; } = new List<AttemptSkillResult>();
}
