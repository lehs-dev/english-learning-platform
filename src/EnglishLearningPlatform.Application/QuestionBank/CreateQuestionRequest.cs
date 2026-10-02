using EnglishLearningPlatform.Domain.Enums;
namespace EnglishLearningPlatform.Application.QuestionBank;
public sealed record CreateaQuestionRequest
{
    public string Content { get; init; } = string.Empty;
    public EnglishSkill? PrimarySkill { get; init; }
    public string? Level { get; init; }
    public string? Difficulty { get; init; }
    public string? Explanation { get; init; }
    public Guid? StimulusId { get; init; }
    public IReadOnlyList<QuestionOptionRequest> Options { get; init; } = Array.Empty<QuestionOptionRequest>();
}