namespace EnglishLearningPlatform.Application.Assessment;

public sealed class WeightedExamScoringStrategy : IScoringStrategy
{
    public ScoringResult CalculateScore(
        IReadOnlyList<QuestionScoringItem> questions,
        IReadOnlyDictionary<Guid, Guid?> studentResponses) =>
        ScoringCalculation.Calculate(questions, studentResponses, question => question.Points);
}
