using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class Question : BaseEntity
{
    public Guid TeacherUserId { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public QuestionType Type { get; set; }
    public EnglishSkill Skill { get; set; }
    public int Difficulty { get; set; } = 1;
    public string? Explanation { get; set; }

    public ICollection<AnswerOption> AnswerOptions { get; set; } = new List<AnswerOption>();
    public ICollection<ExamQuestion> ExamQuestions { get; set; } = new List<ExamQuestion>();
}
