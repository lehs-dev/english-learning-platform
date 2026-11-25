namespace EnglishLearningPlatform.Application.Assessment;

public sealed record QuestionResponse(Guid QuestionId, Guid? SelectedAnswerOptionId,
    string? TextAnswer = null, string? StructuredAnswerJson = null);

public sealed record AssessmentResponse(Guid AssessmentQuestionId, Guid? SelectedOptionId,
    string? TextAnswer = null, string? StructuredAnswerJson = null);

public sealed record ScoringResult(decimal Score, decimal MaxScore, decimal Percentage);
