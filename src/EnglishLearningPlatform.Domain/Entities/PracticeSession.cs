using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

// Một phiên làm Practice, không phải Attempt của Assessment và không giới hạn lượt.
public sealed class PracticeSession : BaseEntity
{
    public Guid StudentUserId { get; set; }
    public Guid PracticeId { get; set; }
    public Practice Practice { get; set; } = null!;
    public int Version { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
}
