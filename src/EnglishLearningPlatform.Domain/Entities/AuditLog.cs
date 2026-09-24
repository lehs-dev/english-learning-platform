using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class AuditLog : BaseEntity
{
    public Guid? ActorUserId { get; set; }
    public Guid? TargetUserId { get; set; }
    public string ActorSnapshot { get; set; } = string.Empty;
    public string? TargetSnapshot { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? BeforeData { get; set; }
    public string? AfterData { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
