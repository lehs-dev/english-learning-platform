namespace EnglishLearningPlatform.Application.Assessment;

public sealed class EqualWeightPracticeScoringStrategy : IPracticeScoringStrategy
{
    public ScoringResult CalculateScore(
        IReadOnlyList<QuestionScoringItem> questions,
        IReadOnlyDictionary<Guid, Guid?> studentResponses) =>
        ScoringCalculation.Calculate(questions, studentResponses, _ => 1m);
}
