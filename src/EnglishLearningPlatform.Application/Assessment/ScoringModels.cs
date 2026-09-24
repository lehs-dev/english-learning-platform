namespace EnglishLearningPlatform.Application.Assessment;

public sealed record QuestionResponse(Guid QuestionId, Guid? SelectedAnswerOptionId);

public sealed record ScoringResult( decimal Score, decimal MaxScore, decimal Percentage);
