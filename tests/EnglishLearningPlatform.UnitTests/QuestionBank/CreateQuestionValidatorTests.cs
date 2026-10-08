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
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankContent_ReturnsRequiredError(string? content)
    {
        var request = validRequest with { Content = content! };
        var errors = _validator.Validate(request);
        Assert.Contains(errors, error =>
        error.Field == nameof(CreateQuestionRequest.Content)
        && error.Code == "required");
    }
    [Fact]
    public void Validate_ContentAtLimit_ReturnsNoErrors()
    {
        var request = validRequest with { Content = new string('a', 4000) };
        var errors = _validator.Validate(request);
        Assert.Empty(errors);
    }
    [Fact]
    public void Validate_ContentOverLimit_ReturnsMaxLengthError()
    {
        var request = validRequest with {Content = new string('a',4001)};
        var errors = _validator.Validate(request);
        Assert.Contains(errors, error =>
        error.Field == nameof(CreateQuestionRequest.Content)
        && error.Code == "max_length");

    }
    [Fact]
    public void Validate_MissingPrimarySkill_ReturnsInvalidError()
    {
        var request = validRequest with
        {
            PrimarySkill = null
        };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, error =>
            error.Field == nameof(CreateQuestionRequest.PrimarySkill)
            && error.Code == "invalid");
    }
    [Theory]
[InlineData(0)]
[InlineData(-1)]
[InlineData(999)]
    public void Validate_UndefinedPrimarySkill_ReturnsInvalidError(int value)
    {
        var request = validRequest with
        {
            PrimarySkill = (EnglishSkill)value
        };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, error =>
            error.Field == nameof(CreateQuestionRequest.PrimarySkill)
            && error.Code == "invalid");
    }
    [Theory]
    [InlineData(EnglishSkill.Vocabulary)]
    [InlineData(EnglishSkill.Grammar)]
    [InlineData(EnglishSkill.Reading)]
    [InlineData(EnglishSkill.Listening)]
    public void Validate_ValidPrimarySkill_ReturnsNoErrors(EnglishSkill skill)
    {
        var request = validRequest with
        {
            PrimarySkill = skill
        };

        var errors = _validator.Validate(request);

        Assert.Empty(errors);
    }
}