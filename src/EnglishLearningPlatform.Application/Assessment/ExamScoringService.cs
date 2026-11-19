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
            .ToDictionary(x => x.Key, x => x.Last());
        var correctResponses = new Dictionary<Guid, Guid?>();
        // The strategy only sees whether the response earned the question's weight.
        // A question ID is the matching token after its own answer format has been graded.
        var questions = assessment.Questions.Select(question => new QuestionScoringItem(
            question.QuestionId,
            question.Points,
            [question.QuestionId]))
            .ToList();
        foreach (var question in assessment.Questions)
        {
            if (responseMap.TryGetValue(question.QuestionId, out var response) &&
                AssessmentAnswerScoring.IsCorrect(question.Question, response))
                correctResponses[question.QuestionId] = question.QuestionId;
        }

        return scoringStrategy.CalculateScore(questions, correctResponses);
    }
}
