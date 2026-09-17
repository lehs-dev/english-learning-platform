using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class ExamQuestion : BaseEntity
{
    public Guid ExamId { get; set; }
    public Exam Exam { get; set; } = null!;
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public decimal Points { get; set; } = 1m;
    public int OrderNo { get; set; }
}
