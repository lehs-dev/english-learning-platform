using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class AttemptAnswer : BaseEntity
{
    public Guid AttemptId { get; set; }
    public Attempt Attempt { get; set; } = null!;
    public Guid AssessmentQuestionId { get; set; }
    public AssessmentQuestion AssessmentQuestion { get; set; } = null!;
    public Guid SelectedOptionId { get; set; }
    public QuestionOption SelectedOption { get; set; } = null!;
    public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
