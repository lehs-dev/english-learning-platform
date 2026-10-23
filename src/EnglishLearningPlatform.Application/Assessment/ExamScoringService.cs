using AssessmentEntity = EnglishLearningPlatform.Domain.Entities.Assessment;

namespace EnglishLearningPlatform.Application.Assessment;

public sealed class ExamScoringService : IExamScoringService
{
    private readonly IScoringStrategy scoringStrategy;

    public ExamScoringService() : this(new WeightedExamScoringStrategy())
    {
    }

    public ExamScoringService(IScoringStrategy scoringStrategy)
    {
        ArgumentNullException.ThrowIfNull(scoringStrategy);
        this.scoringStrategy = scoringStrategy;
    }

    public ScoringResult Score(
        AssessmentEntity assessment,
        IReadOnlyCollection<QuestionResponse> responses)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(responses);

        var responseMap = responses
            .GroupBy(x => x.QuestionId)
            .ToDictionary(x => x.Key, x => x.Last().SelectedAnswerOptionId);
        var questions = assessment.Questions.Select(question => new QuestionScoringItem(
            question.QuestionId,
            question.Points,
            question.Question.Options.Where(option => option.IsCorrect).Select(option => option.Id).ToList()))
            .ToList();

        return scoringStrategy.CalculateScore(questions, responseMap);
    }
}
