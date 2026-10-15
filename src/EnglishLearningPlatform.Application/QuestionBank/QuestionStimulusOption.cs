using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.QuestionBank;

public sealed record QuestionStimulusOption(
    Guid Id,
    StimulusType Type,
    string? Title);
