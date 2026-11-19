using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Application.Practice;
namespace EnglishLearningPlatform.Application.QuestionBank;

public sealed record CreateQuestionRequest
{
    public QuestionType QuestionType { get; init; } = QuestionType.MultipleChoice;
    public IReadOnlyList<string> AcceptedAnswers { get; init; } = [];
    public IReadOnlyList<StructuredQuestionAuthorItem> StructuredItems { get; init; } = [];
    public string Content { get; init; } = string.Empty;
    public EnglishSkill? PrimarySkill { get; init; }
    public string? Level { get; init; }
    public string? Explanation { get; init; }
    public Guid? StimulusId { get; init; }
    public IReadOnlyList<QuestionOptionRequest> Options { get; init; } = Array.Empty<QuestionOptionRequest>();
}
