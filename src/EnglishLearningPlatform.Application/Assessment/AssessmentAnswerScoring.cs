using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Assessment;

public static class AssessmentAnswerScoring
{
    public static bool IsCorrect(Question question, QuestionResponse response) => question.QuestionType switch
    {
        QuestionType.MultipleChoice => response.SelectedAnswerOptionId.HasValue &&
            response.TextAnswer is null && response.StructuredAnswerJson is null &&
            question.Options.Any(option => option.Id == response.SelectedAnswerOptionId && option.IsCorrect),
        QuestionType.FillBlank => !response.SelectedAnswerOptionId.HasValue && response.StructuredAnswerJson is null &&
            AnswerNormalizer.Matches(response.TextAnswer, question.AcceptedAnswers.Select(answer => answer.AnswerText)),
        QuestionType.Matching or QuestionType.Ordering => !response.SelectedAnswerOptionId.HasValue && response.TextAnswer is null &&
            StructuredQuestionEvaluator.Evaluate(question, response.StructuredAnswerJson) is { IsValid: true, IsCorrect: true },
        _ => false
    };
}
