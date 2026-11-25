using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Application.Practice;

namespace EnglishLearningPlatform.Application.QuestionBank;

public sealed record QuestionEditor(
    Guid Id,
    string Content,
    EnglishSkill PrimarySkill,
    string? Level,
    string? Explanation,
    Guid? StimulusId,
    QuestionStatus Status,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<QuestionOptionItem> Options,
    bool ScoringContentLocked,
    string? LockReason)
{
    public QuestionType QuestionType { get; init; } = QuestionType.MultipleChoice;
    public IReadOnlyList<string> AcceptedAnswers { get; init; } = [];
    public IReadOnlyList<StructuredQuestionAuthorItem> StructuredItems { get; init; } = [];
}
