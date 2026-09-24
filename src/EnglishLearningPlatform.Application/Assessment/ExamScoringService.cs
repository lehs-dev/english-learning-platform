using AssessmentEntity = EnglishLearningPlatform.Domain.Entities.Assessment;

namespace EnglishLearningPlatform.Application.Assessment;

public sealed class ExamScoringService : IExamScoringService
{
    public ScoringResult Score(
        AssessmentEntity assessment,
        IReadOnlyCollection<QuestionResponse> responses)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(responses);

        var responseMap = responses
            .GroupBy(x => x.QuestionId)
            .ToDictionary(x => x.Key, x => x.Last());

        decimal score = 0m;
        decimal maxScore = 0m;

        foreach (var assessmentQuestion in assessment.Questions)
        {
            maxScore += assessmentQuestion.Points;

            if (!responseMap.TryGetValue(assessmentQuestion.QuestionId, out var response))
                continue;

            var isCorrect = assessmentQuestion.Question.Options
                .Any(option =>
                    option.IsCorrect &&
                    option.Id == response.SelectedAnswerOptionId);

            if (isCorrect)
                score += assessmentQuestion.Points;
        }

        var percentage = maxScore <= 0m
            ? 0m
            : Math.Round(score / maxScore * 100m, 2);

        return new ScoringResult(score, maxScore, percentage);
    }
}
