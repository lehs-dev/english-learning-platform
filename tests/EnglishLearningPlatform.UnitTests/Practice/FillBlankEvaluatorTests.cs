using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Practice;

public sealed class FillBlankEvaluatorTests
{
    [Theory]
    [InlineData(" lived ", "lived", true)]
    [InlineData("WAS  \t living", "was living", true)]
    [InlineData("cafe\u0301", "café", true)]
    [InlineData("don't", "do not", false)]
    [InlineData("hello!", "hello", false)]
    [InlineData("lveid", "lived", false)]
    [InlineData("", "", false)]
    [InlineData("   ", "lived", false)]
    public void Matches_OnlyNormalizesSpecifiedTextRules(string input, string accepted, bool expected) =>
        Assert.Equal(expected, AnswerNormalizer.Matches(input, [accepted]));

    [Fact]
    public void MultipleAcceptedAnswers_AreExplicitVariants() =>
        Assert.True(AnswerNormalizer.Matches("  WAS living  ", ["lived", "was living"]));

    [Fact]
    public void FillBlank_RequiresAcceptedAnswersAndMatchingRequiresPairs()
    {
        var validator = new CreateQuestionValidator();
        var request = new CreateQuestionRequest { Content = "I ___ here.", PrimarySkill = EnglishSkill.Grammar, QuestionType = QuestionType.FillBlank };
        Assert.Contains(validator.Validate(request), e => e.Field == nameof(request.AcceptedAnswers));
        Assert.Empty(validator.Validate(request with { AcceptedAnswers = ["lived"] }));
        Assert.Empty(validator.Validate(request with { AcceptedAnswers = ["lived"], Options = null! }));
        Assert.NotEmpty(validator.Validate(request with { AcceptedAnswers = [new string('a', 501)] }));
        Assert.NotEmpty(validator.Validate(request with { QuestionType = QuestionType.Matching }));
    }
}
