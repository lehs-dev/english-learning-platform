using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class GamificationEvent : BaseEntity
{
    public Guid StudentUserId { get; set; }
    public GamificationEventType EventType { get; set; }
    public Guid SourceId { get; set; }
    public string WindowKey { get; set; } = string.Empty;
    public int Xp { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}
