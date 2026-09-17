using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class StudentAnswer : BaseEntity
{
    public Guid ExamAttemptId { get; set; }
    public ExamAttempt ExamAttempt { get; set; } = null!;
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public Guid? SelectedAnswerOptionId { get; set; }
    public AnswerOption? SelectedAnswerOption { get; set; }
    public string? TextAnswer { get; set; }
    public bool? IsCorrect { get; set; }
    public decimal AwardedPoints { get; set; }
}
