using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Assessment;

public sealed class ExamScoringService : IExamScoringService
{
    public ScoringResult Score(Exam exam, IReadOnlyCollection<QuestionResponse> responses)
    {
        ArgumentNullException.ThrowIfNull(exam);
        ArgumentNullException.ThrowIfNull(responses);

        var responseMap = responses
            .GroupBy(x => x.QuestionId)
            .ToDictionary(x => x.Key, x => x.Last());

        decimal score = 0m;
        decimal maxScore = 0m;

        foreach (var examQuestion in exam.ExamQuestions)
        {
            maxScore += examQuestion.Points;
            if (!responseMap.TryGetValue(examQuestion.QuestionId, out var response))
                continue;

            var question = examQuestion.Question;
            var isCorrect = question.Type switch
            {
                QuestionType.MultipleChoice or QuestionType.TrueFalse =>
                    question.AnswerOptions.Any(o => o.IsCorrect && o.Id == response.SelectedAnswerOptionId),
                QuestionType.FillInBlank =>
                    question.AnswerOptions.Any(o =>
                        o.IsCorrect &&
                        !string.IsNullOrWhiteSpace(response.TextAnswer) &&
                        string.Equals(o.Text.Trim(), response.TextAnswer.Trim(), StringComparison.OrdinalIgnoreCase)),
                _ => false
            };

            if (isCorrect)
                score += examQuestion.Points;
        }

        var percentage = maxScore <= 0 ? 0 : Math.Round(score / maxScore * 100m, 2);
        return new ScoringResult(score, maxScore, percentage);
    }
}
