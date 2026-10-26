using EnglishLearningPlatform.Domain.Enums;

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
    string? LockReason);
