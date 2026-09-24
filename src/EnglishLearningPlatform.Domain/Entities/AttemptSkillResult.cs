using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class AttemptSkillResult : BaseEntity
{
    public Guid AttemptId { get; set; }
    public Attempt Attempt { get; set; } = null!;
    public EnglishSkill Skill { get; set; }
    public decimal Score { get; set; }
    public int QuestionCount { get; set; }
}
