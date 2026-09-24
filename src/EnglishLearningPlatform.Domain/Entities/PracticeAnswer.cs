using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class PracticeAnswer : BaseEntity
{
    public Guid PracticeSubmissionId { get; set; }
    public PracticeSubmission PracticeSubmission { get; set; } = null!;
    public Guid PracticeQuestionId { get; set; }
    public PracticeQuestion PracticeQuestion { get; set; } = null!;
    public Guid SelectedOptionId { get; set; }
    public QuestionOption SelectedOption { get; set; } = null!;
}
