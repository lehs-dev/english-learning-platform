using System.Text.Json;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Practice;

public sealed class StructuredQuestionEvaluatorTests
{
    [Fact]
    public void Matching_GradesOneToOnePairsWithoutExposingCorrectRelationships()
    {
        var question = Make(QuestionType.Matching, [new("Cat", "Mèo"), new("Dog", "Chó")]);
        var prompt = StructuredQuestionEvaluator.GetPrompt(question)!;
        Assert.Equal(["Cat", "Dog"], prompt.Items.Select(item => item.Text));
        Assert.Equal(prompt.Matches.OrderBy(item => item.Id), prompt.Matches);
        Assert.Empty(prompt.Items.Select(item => item.Id).Intersect(prompt.Matches.Select(item => item.Id)));
        var publicJson = JsonSerializer.Serialize(prompt, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("matchId", publicJson);
        Assert.DoesNotContain("correct", publicJson, StringComparison.OrdinalIgnoreCase);

        Guid Right(string text) => Assert.Single(prompt.Matches, choice => choice.Text == text).Id;
        var correct = JsonSerializer.Serialize(new[]
        {
            new { leftId = prompt.Items[0].Id, rightId = Right("Mèo") },
            new { leftId = prompt.Items[1].Id, rightId = Right("Chó") }
        });
        var wrong = JsonSerializer.Serialize(new[]
        {
            new { leftId = prompt.Items[0].Id, rightId = Right("Chó") },
            new { leftId = prompt.Items[1].Id, rightId = Right("Mèo") }
        });
        var fullCredit = StructuredQuestionEvaluator.Evaluate(question, correct);
        var zeroCredit = StructuredQuestionEvaluator.Evaluate(question, wrong);
        Assert.True(fullCredit.IsValid && fullCredit.IsCorrect);
        Assert.True(zeroCredit.IsValid);
        Assert.False(zeroCredit.IsCorrect);
        Assert.Equal("Cat ↔ Mèo · Dog ↔ Chó", fullCredit.CorrectAnswer);
        Assert.Equal("Cat ↔ Chó · Dog ↔ Mèo", zeroCredit.SubmittedAnswer);
        Assert.Equal(fullCredit, StructuredQuestionEvaluator.Evaluate(question, fullCredit.CanonicalJson));
    }

    [Fact]
    public void Ordering_OnlyGrantsCreditForCompleteCorrectSequence()
    {
        var question = Make(QuestionType.Ordering, [new("First"), new("Second"), new("Third")]);
        var prompt = StructuredQuestionEvaluator.GetPrompt(question)!;
        Assert.Empty(prompt.Matches);
        Assert.Equal(prompt.Items.OrderBy(item => item.Id), prompt.Items);
        Guid Choice(string text) => Assert.Single(prompt.Items, item => item.Text == text).Id;
        var correct = new[] { Choice("First"), Choice("Second"), Choice("Third") };

        Assert.True(StructuredQuestionEvaluator.Evaluate(question, JsonSerializer.Serialize(correct)).IsCorrect);
        var wrong = StructuredQuestionEvaluator.Evaluate(question, JsonSerializer.Serialize(correct.Reverse()));
        Assert.True(wrong.IsValid);
        Assert.False(wrong.IsCorrect);
        Assert.Equal("Third → Second → First", wrong.SubmittedAnswer);
        Assert.Equal("First → Second → Third", wrong.CorrectAnswer);
        Assert.False(StructuredQuestionEvaluator.Evaluate(question, JsonSerializer.Serialize(correct.Take(2))).IsValid);
        Assert.False(StructuredQuestionEvaluator.Evaluate(question, JsonSerializer.Serialize(new[] { correct[0], correct[0], correct[2] })).IsValid);
        Assert.False(StructuredQuestionEvaluator.Evaluate(question, JsonSerializer.Serialize(new[] { correct[0], Guid.NewGuid(), correct[2] })).IsValid);
    }

    [Fact]
    public void Matching_RejectsForgedDuplicateAndMixedAnswerFields()
    {
        var question = Make(QuestionType.Matching, [new("Cat", "Mèo"), new("Dog", "Chó")]);
        var prompt = StructuredQuestionEvaluator.GetPrompt(question)!;
        Guid left = prompt.Items[0].Id;
        Guid secondLeft = prompt.Items[1].Id;
        Guid right = prompt.Matches[0].Id;
        Guid secondRight = prompt.Matches[1].Id;
        string[] invalid =
        [
            $$"""[{"leftId":"{{left}}","rightId":"{{right}}"},{"leftId":"{{left}}","rightId":"{{secondRight}}"}]""",
            $$"""[{"leftId":"{{left}}","rightId":"{{right}}"},{"leftId":"{{secondLeft}}","rightId":"{{right}}"}]""",
            $$"""[{"leftId":"{{left}}","rightId":"{{right}}","rightId":"{{secondRight}}"},{"leftId":"{{secondLeft}}","rightId":"{{secondRight}}"}]""",
            $$"""[{"leftId":"{{left}}","rightId":"{{right}}","correct":true},{"leftId":"{{secondLeft}}","rightId":"{{secondRight}}"}]""",
            $$"""["{{left}}","{{secondLeft}}"]""",
            "[{invalid json]"
        ];

        foreach (string response in invalid)
        {
            var evaluation = StructuredQuestionEvaluator.Evaluate(question, response);
            Assert.False(evaluation.IsValid);
            Assert.Null(evaluation.CanonicalJson);
            Assert.Empty(evaluation.CorrectAnswer);
        }
    }

    [Fact]
    public void BlankResponseIsUnansweredAndMalformedPersistedDefinitionFailsClosed()
    {
        var question = Make(QuestionType.Ordering, [new("One"), new("Two")]);
        var blank = StructuredQuestionEvaluator.Evaluate(question, null);
        Assert.True(blank.IsValid);
        Assert.False(blank.IsCorrect);
        Assert.Null(blank.SubmittedAnswer);
        Assert.Null(blank.CanonicalJson);
        Assert.False(StructuredQuestionEvaluator.Evaluate(question, new string(' ', StructuredQuestionEvaluator.MaxAnswerLength) + "[]").IsValid);

        question.StructuredDefinitionJson = "[{\"text\":\"Unkeyed\"}]";
        Assert.Null(StructuredQuestionEvaluator.GetPrompt(question));
        Assert.False(StructuredQuestionEvaluator.Evaluate(question, null).IsValid);
    }

    [Fact]
    public void Validator_AcceptsSavedStructuredQuestionAndRejectsAmbiguousOrMixedDefinitions()
    {
        var create = new CreateQuestionValidator();
        var request = new CreateQuestionRequest
        {
            Content = "Match the animals",
            PrimarySkill = EnglishSkill.Vocabulary,
            QuestionType = QuestionType.Matching,
            StructuredItems = [new("Cat", "Mèo"), new("Dog", "Chó")]
        };
        Assert.Empty(create.Validate(request));
        Assert.Contains(create.Validate(request with { StructuredItems = [new("Cat", "Mèo"), new("Dog", " mèo ")] }),
            error => error.Field == nameof(request.StructuredItems));
        Assert.Contains(create.Validate(request with { AcceptedAnswers = ["Mèo"] }), error => error.Code == "mixed_answers");
        Assert.Contains(create.Validate(request with { StructuredItems = [new("Cat", "Mèo")] }),
            error => error.Field == nameof(request.StructuredItems));

        var question = Make(request.QuestionType, request.StructuredItems);
        var usage = new QuestionUsageValidator(create);
        Assert.Empty(usage.ValidateForNewUse(question.OwnerTeacherUserId, question));
        question.StructuredDefinitionJson = "[]";
        Assert.NotEmpty(usage.ValidateForNewUse(question.OwnerTeacherUserId, question));
    }

    private static Question Make(QuestionType type, IReadOnlyList<StructuredQuestionAuthorItem> items) => new()
    {
        OwnerTeacherUserId = Guid.NewGuid(),
        Content = "Structured question",
        PrimarySkill = EnglishSkill.Vocabulary,
        QuestionType = type,
        StructuredDefinitionJson = StructuredQuestionEvaluator.CreateDefinition(type, items)
    };
}
