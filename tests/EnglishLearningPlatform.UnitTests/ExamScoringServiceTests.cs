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

    [Fact]
    public void Score_FractionalPointsAndBlankAnswers_CountEveryQuestionInMaximumScore()
    {
        var questions = new[]
        {
            CreateQuestion("Câu trả lời đúng"),
            CreateQuestion("Câu trả lời sai"),
            CreateQuestion("Câu được bỏ chọn"),
            CreateQuestion("Câu chưa trả lời")
        };
        var points = new[] { 1.25m, 2.5m, 0.75m, 3m };
        var assessment = new Assessment { Title = "Đề kiểm tra điểm lẻ" };

        // Test công thức độc lập, không kiểm tra điều kiện Publish tối thiểu 5 câu.
        for (var i = 0; i < questions.Length; i++)
        {
            assessment.Questions.Add(new AssessmentQuestion
            {
                QuestionId = questions[i].Id,
                Question = questions[i],
                Points = points[i]
            });
        }

        var responses = new[]
        {
            new QuestionResponse(questions[0].Id, questions[0].Options.Single(o => o.IsCorrect).Id),
            new QuestionResponse(questions[1].Id, questions[1].Options.Single(o => !o.IsCorrect).Id),
            new QuestionResponse(questions[2].Id, null)
        };

        var result = new ExamScoringService().Score(assessment, responses);

        // Sai, bỏ chọn và chưa trả lời đều được 0, nhưng points vẫn nằm trong mẫu số.
        Assert.Equal(1.25m, result.Score);
        Assert.Equal(7.5m, result.MaxScore);
        Assert.Equal(16.67m, result.Percentage);
    }

    [Fact]
    public void Score_SharedQuestions_UsePointsOfEachAssessmentWithoutChangingQuestionBank()
    {
        var firstQuestion = CreateQuestion("Câu dùng chung thứ nhất");
        var secondQuestion = CreateQuestion("Câu dùng chung thứ hai");
        var questions = new[] { firstQuestion, secondQuestion };
        var questionsBefore = questions.Select(q => (q.Id, q.Content, q.PrimarySkill)).ToArray();
        var optionsBefore = questions.SelectMany(q => q.Options)
            .Select(o => (o.Id, o.Content, o.IsCorrect, o.OrderIndex)).ToArray();

        // Cùng Question, nhưng mỗi đề lưu points riêng tại AssessmentQuestion.
        var firstAssessment = new Assessment
        {
            Title = "Đề thứ nhất",
            Questions =
            [
                new AssessmentQuestion { QuestionId = firstQuestion.Id, Question = firstQuestion, Points = 1m },
                new AssessmentQuestion { QuestionId = secondQuestion.Id, Question = secondQuestion, Points = 3m }
            ]
        };
        var secondAssessment = new Assessment
        {
            Title = "Đề thứ hai",
            Questions =
            [
                new AssessmentQuestion { QuestionId = firstQuestion.Id, Question = firstQuestion, Points = 3m },
                new AssessmentQuestion { QuestionId = secondQuestion.Id, Question = secondQuestion, Points = 1m }
            ]
        };
        var responses = new[]
        {
            new QuestionResponse(firstQuestion.Id, firstQuestion.Options.Single(o => o.IsCorrect).Id),
            new QuestionResponse(secondQuestion.Id, secondQuestion.Options.Single(o => !o.IsCorrect).Id)
        };
        var scoring = new ExamScoringService();

        var firstResult = scoring.Score(firstAssessment, responses);
        var secondResult = scoring.Score(secondAssessment, responses);

        Assert.Equal(1m, firstResult.Score);
        Assert.Equal(4m, firstResult.MaxScore);
        Assert.Equal(25m, firstResult.Percentage);
        Assert.Equal(3m, secondResult.Score);
        Assert.Equal(4m, secondResult.MaxScore);
        Assert.Equal(75m, secondResult.Percentage);

        // Chấm đề không được sửa nội dung, PrimarySkill hay đáp án của Question dùng chung.
        Assert.Equal(questionsBefore, questions.Select(q => (q.Id, q.Content, q.PrimarySkill)).ToArray());
        Assert.Equal(optionsBefore, questions.SelectMany(q => q.Options)
            .Select(o => (o.Id, o.Content, o.IsCorrect, o.OrderIndex)).ToArray());
    }

    // Mỗi câu mẫu là MCQ hợp lệ: có hai lựa chọn và đúng một đáp án đúng.
    private static Question CreateQuestion(string content)
    {
        return new Question
        {
            Content = content,
            PrimarySkill = EnglishSkill.Grammar,
            Options =
            [
                new QuestionOption { Content = "Đáp án đúng", IsCorrect = true, OrderIndex = 0 },
                new QuestionOption { Content = "Đáp án sai", IsCorrect = false, OrderIndex = 1 }
            ]
        };
    }
}
