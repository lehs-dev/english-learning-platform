using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class AssessmentQuestion : BaseEntity
{
    public Guid AssessmentId { get; set; }
    public Assessment Assessment { get; set; } = null!;
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public int OrderIndex { get; set; }
    public decimal Points { get; set; } = 1m;

    public ICollection<AttemptAnswer> Answers { get; set; } = new List<AttemptAnswer>();
}
