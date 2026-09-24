using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

namespace EnglishLearningPlatform.UnitTests;

public sealed class ExamScoringServiceTests
{
    [Fact]
    public void Score_ReturnsFullPoints_WhenSelectedOptionIsCorrect()
    {
        var correct = new QuestionOption { Content = "B", IsCorrect = true };
        var wrong = new QuestionOption { Content = "A" };

        var question = new Question
        {
            Content = "Choose B",
            PrimarySkill = EnglishSkill.Grammar,
            Options = [wrong, correct]
        };

        var assessment = new Assessment
        {
            Title = "Grammar Test",
            Questions =
            [
                new AssessmentQuestion
                {
                    QuestionId = question.Id,
                    Question = question,
                    Points = 2m
                }
            ]
        };

        var result = new ExamScoringService().Score(
            assessment,
            [new QuestionResponse(question.Id, correct.Id)]);

        Assert.Equal(2m, result.Score);
        Assert.Equal(2m, result.MaxScore);
        Assert.Equal(100m, result.Percentage);
    }
}
