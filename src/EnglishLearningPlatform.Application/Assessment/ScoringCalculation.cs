namespace EnglishLearningPlatform.Application.Assessment;

internal static class ScoringCalculation
{
    public static ScoringResult Calculate(
        IReadOnlyList<QuestionScoringItem> questions,
        IReadOnlyDictionary<Guid, Guid?> studentResponses,
        Func<QuestionScoringItem, decimal> weight)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(studentResponses);

        decimal score = 0m;
        decimal maxScore = 0m;

        foreach (var question in questions)
        {
            var points = weight(question);
            maxScore += points;

            if (studentResponses.TryGetValue(question.QuestionId, out var selectedOptionId) &&
                selectedOptionId.HasValue && question.CorrectOptionIds.Contains(selectedOptionId.Value))
            {
                score += points;
            }
        }

        var percentage = maxScore <= 0m ? 0m : Math.Round(score / maxScore * 100m, 2);
        return new ScoringResult(score, maxScore, percentage);
    }
}
