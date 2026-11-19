using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class LearningPlanItem : BaseEntity
{
    public Guid PlanId { get; set; }
    public LearningPlan Plan { get; set; } = null!;
    public int DayIndex { get; set; }
    public LearningPlanTargetType TargetType { get; set; }
    public Guid TargetId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int EstimatedMinutes { get; set; } = 10;
    public EnglishSkill Skill { get; set; }
    public LearningPlanItemStatus Status { get; set; } = LearningPlanItemStatus.Pending;
    public int Position { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
