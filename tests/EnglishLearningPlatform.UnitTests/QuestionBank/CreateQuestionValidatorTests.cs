using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Application.QuestionBank;
using Xunit;
namespace EnglishLearningPlatform.UnitTests.QuestionBank;
public sealed class CreateQuestionValidatorTests
{
    private readonly CreateQuestionValidator _validator = new();
    private static CreateQuestionRequest validRequest = new()
    {
        Content = "Choose the correct answer.",
        PrimarySkill = EnglishSkill.Reading,
        Level = "Beginner",
        Difficulty = "Easy",
        Options = 
        [
            new QuestionOptionRequest("Option 1", false),
            new QuestionOptionRequest("Option 2", true)
        ]
    };
    [Fact]
    public void Validate_ValidRequest_ReturnsNoErrors()
    {
        var errors = _validator.Validate(validRequest);

        Assert.Empty(errors);
    }
    [Fact]
    public void Validate_InvalidRequest_ReturnsErrors()
    {
        var errors = _validator.Validate(validRequest with 
        {
            Content = "",
            PrimarySkill = null,
            Level = new string('a', 33),
            Difficulty = new string('b', 33),
            Options = [new QuestionOptionRequest("Option 1", false)]
        });
        Assert.NotEmpty(errors);
    }
}
