namespace EnglishLearningPlatform.Application.Assessment;

public interface IScoringStrategy
{
    ScoringResult CalculateScore(
        IReadOnlyList<QuestionScoringItem> questions,
        IReadOnlyDictionary<Guid, Guid?> studentResponses);
}

// A distinct contract lets practice replace its scoring policy independently of exams.
public interface IPracticeScoringStrategy : IScoringStrategy
{
}

public sealed record QuestionScoringItem(
    Guid QuestionId,
    decimal Points,
    IReadOnlyCollection<Guid> CorrectOptionIds);
