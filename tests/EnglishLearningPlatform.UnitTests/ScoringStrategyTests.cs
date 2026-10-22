using EnglishLearningPlatform.Application.Assessment;
using Xunit;

namespace EnglishLearningPlatform.UnitTests;

public sealed class ScoringStrategyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CalculateScore_CountsWrongBlankAndMissingAnswersInMaximum(bool weighted)
    {
        var correctOption = Guid.NewGuid();
        var questions = new[]
        {
            new QuestionScoringItem(Guid.NewGuid(), 1.25m, [correctOption]),
            new QuestionScoringItem(Guid.NewGuid(), 2.5m, [Guid.NewGuid()]),
            new QuestionScoringItem(Guid.NewGuid(), 0.75m, [Guid.NewGuid()]),
            new QuestionScoringItem(Guid.NewGuid(), 3m, [Guid.NewGuid()])
        };
        var responses = new Dictionary<Guid, Guid?>
        {
            [questions[0].QuestionId] = correctOption,
            [questions[1].QuestionId] = Guid.NewGuid(),
            [questions[2].QuestionId] = null,
            [Guid.NewGuid()] = correctOption
        };

        var result = Strategy(weighted).CalculateScore(questions, responses);

        Assert.Equal(weighted ? new ScoringResult(1.25m, 7.5m, 16.67m) : new ScoringResult(1m, 4m, 25m), result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CalculateScore_EmptyQuestionSet_ReturnsZero(bool weighted)
    {
        var result = Strategy(weighted).CalculateScore([], new Dictionary<Guid, Guid?>());

        Assert.Equal(new ScoringResult(0m, 0m, 0m), result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CalculateScore_OptionMustBeCorrectForTheRespondedQuestion(bool weighted)
    {
        var firstCorrect = Guid.NewGuid();
        var secondCorrect = Guid.NewGuid();
        var questions = new[]
        {
            new QuestionScoringItem(Guid.NewGuid(), 3m, [firstCorrect]),
            new QuestionScoringItem(Guid.NewGuid(), 7m, [secondCorrect]),
            new QuestionScoringItem(Guid.NewGuid(), 2m, [])
        };
        var responses = new Dictionary<Guid, Guid?>
        {
            [questions[0].QuestionId] = secondCorrect,
            [questions[1].QuestionId] = firstCorrect,
            [questions[2].QuestionId] = firstCorrect
        };

        var result = Strategy(weighted).CalculateScore(questions, responses);

        Assert.Equal(0m, result.Score);
        Assert.Equal(weighted ? 12m : 3m, result.MaxScore);
        Assert.Equal(0m, result.Percentage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CalculateScore_PreservesMidpointToEvenPercentageRounding(bool weighted)
    {
        var questions = Enumerable.Range(0, 32)
            .Select(_ => new QuestionScoringItem(Guid.NewGuid(), 1m, [Guid.NewGuid()])).ToArray();
        var responses = new Dictionary<Guid, Guid?>
        {
            [questions[0].QuestionId] = questions[0].CorrectOptionIds.Single()
        };

        var result = Strategy(weighted).CalculateScore(questions, responses);

        Assert.Equal(3.12m, result.Percentage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void WeightedScore_NonPositiveMaximum_ReturnsZeroPercentage(int points)
    {
        var option = Guid.NewGuid();
        var question = new QuestionScoringItem(Guid.NewGuid(), points, [option]);

        var result = new WeightedExamScoringStrategy().CalculateScore([question],
            new Dictionary<Guid, Guid?> { [question.QuestionId] = option });

        Assert.Equal(new ScoringResult(points, points, 0m), result);
    }

    private static IScoringStrategy Strategy(bool weighted) =>
        weighted ? new WeightedExamScoringStrategy() : new EqualWeightPracticeScoringStrategy();
}
