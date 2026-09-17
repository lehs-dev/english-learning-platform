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
        var correct = new AnswerOption { Text = "B", IsCorrect = true };
        var wrong = new AnswerOption { Text = "A", IsCorrect = false };
        var question = new Question
        {
            Prompt = "Choose B",
            Type = QuestionType.MultipleChoice,
            Skill = EnglishSkill.Grammar,
            AnswerOptions = [wrong, correct]
        };
        var exam = new Exam
        {
            Title = "Grammar Test",
            ExamQuestions = [new ExamQuestion { QuestionId = question.Id, Question = question, Points = 2m }]
        };

        var result = new ExamScoringService().Score(
            exam,
            [new QuestionResponse(question.Id, correct.Id)]);

        Assert.Equal(2m, result.Score);
        Assert.Equal(2m, result.MaxScore);
        Assert.Equal(100m, result.Percentage);
    }
}
