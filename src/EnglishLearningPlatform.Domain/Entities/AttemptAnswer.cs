using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class AttemptAnswer : BaseEntity
{
    public Guid AttemptId { get; set; }
    public Attempt Attempt { get; set; } = null!;
    public Guid AssessmentQuestionId { get; set; }
    public AssessmentQuestion AssessmentQuestion { get; set; } = null!;
    public Guid? SelectedOptionId { get; set; }
    public QuestionOption? SelectedOption { get; set; }
    public string? TextAnswer { get; set; }
    public string? StructuredAnswerJson { get; set; }
    public bool? IsCorrect { get; set; }
    public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
