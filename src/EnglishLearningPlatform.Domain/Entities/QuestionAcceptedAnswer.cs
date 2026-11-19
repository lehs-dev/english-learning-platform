using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class QuestionAcceptedAnswer : BaseEntity
{
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public string AnswerText { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
}
