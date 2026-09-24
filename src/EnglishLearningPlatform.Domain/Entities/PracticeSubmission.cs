using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class PracticeSubmission : BaseEntity
{
    public Guid StudentUserId { get; set; }
    public Guid PracticeId { get; set; }
    public Practice Practice { get; set; } = null!;
    public decimal Score { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<PracticeAnswer> Answers { get; set; } = new List<PracticeAnswer>();
}
