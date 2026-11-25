using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class StudentAchievement : BaseEntity
{
    public Guid StudentUserId { get; set; }
    public string BadgeCode { get; set; } = string.Empty;
    public DateTimeOffset AwardedAtUtc { get; set; }
}
