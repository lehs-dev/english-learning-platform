using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class Question : BaseEntity
{
    public Guid OwnerTeacherUserId { get; set; }
    public Guid? StimulusId { get; set; }
    public Stimulus? Stimulus { get; set; }
    public string Content { get; set; } = string.Empty;
    public EnglishSkill PrimarySkill { get; set; }
    public string? Level { get; set; }
    public string? Difficulty { get; set; }
    public string? Explanation { get; set; }
    public QuestionStatus Status { get; set; } = QuestionStatus.Draft;

    public ICollection<QuestionOption> Options { get; set; } = new List<QuestionOption>();
    public ICollection<PracticeQuestion> PracticeQuestions { get; set; } = new List<PracticeQuestion>();
    public ICollection<AssessmentQuestion> AssessmentQuestions { get; set; } = new List<AssessmentQuestion>();
}
