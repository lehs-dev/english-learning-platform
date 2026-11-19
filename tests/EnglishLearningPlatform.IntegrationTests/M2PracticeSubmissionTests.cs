using System.Text.Json;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class M2PracticeSubmissionTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task StructuredPractice_GradesBothTypesAndPreservesTheOriginalRetakeAnswers()
    {
        using var scope = factory.Services.CreateScope();
        var (student, practice, matching, ordering) = await SeedAsync(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var opened = await service.BeginAsync(student, practice);
        Assert.Equal(LearningAccessResult.Allowed, opened.Access);
        var sheet = opened.Value!;
        Assert.All(sheet.Questions, q => Assert.Empty(q.Options));
        Assert.DoesNotContain("PRIVATE_ANSWER_EXPLANATION", JsonSerializer.Serialize(sheet));
        var matchPrompt = sheet.Questions.Single(q => q.Id == matching).StructuredPrompt!;
        var orderPrompt = sheet.Questions.Single(q => q.Id == ordering).StructuredPrompt!;
        Assert.Equal(4, matchPrompt.Matches.Count);
        Assert.Empty(orderPrompt.Matches);

        var pairs = matchPrompt.Items.Select(item => new
        {
            leftId = item.Id,
            rightId = matchPrompt.Matches.Single(option => option.Text.StartsWith(item.Text[..1], StringComparison.Ordinal)).Id
        });
        var ordered = new[] { "First step", "Last step" }.Select(label => orderPrompt.Items.Single(i => i.Text == label).Id).ToArray();
        var responses = new[]
        {
            new PracticeResponse(matching, null, StructuredAnswerJson: JsonSerializer.Serialize(pairs)),
            new PracticeResponse(ordering, null, StructuredAnswerJson: JsonSerializer.Serialize(ordered))
        };

        Assert.Equal(PracticeSubmitStatus.Saved, (await service.SubmitSessionAsync(student, practice, sheet.SessionId!.Value, responses)).Status);
        var result = (await service.GetResultAsync(student, practice)).Value!;
        Assert.Equal(100m, result.Score);
        Assert.All(result.Questions, q => Assert.True(q.IsCorrect));
        Assert.Contains("First step", result.Questions.Single(q => q.Id == ordering).CorrectAnswer);
        Assert.Contains("PRIVATE_ANSWER_EXPLANATION", result.Questions.Single(q => q.Id == ordering).Explanation);

        db.ChangeTracker.Clear();
        var nextSheet = (await service.BeginAsync(student, practice)).Value!;
        responses[1] = responses[1] with { StructuredAnswerJson = JsonSerializer.Serialize(ordered.Reverse()) };
        Assert.Equal(PracticeSubmitStatus.Saved, (await service.SubmitSessionAsync(student, practice, nextSheet.SessionId!.Value, responses)).Status);
        Assert.Equal(50m, (await service.GetResultAsync(student, practice)).Value!.Score);
        var history = await db.PracticeSubmissionHistories.AsNoTracking().Include(h => h.Answers).SingleAsync(h => h.PracticeId == practice);
        Assert.Equal(100m, history.Score);
        var oldOrder = history.Answers.Single(a => a.PracticeQuestionId == ordering);
        Assert.True(oldOrder.IsCorrect);
        Assert.Equal(ordered, JsonSerializer.Deserialize<Guid[]>(oldOrder.StructuredAnswerJson!));
        Assert.True(history.Answers.Single(a => a.PracticeQuestionId == matching).SelectedAnswer!.Length > 1000);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    [InlineData("mixed-payload")]
    public async Task InvalidStructuredResponse_DoesNotWriteSubmission(string fault)
    {
        using var scope = factory.Services.CreateScope();
        var (student, practice, matching, ordering) = await SeedAsync(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sheet = (await service.GetSheetAsync(student, practice)).Value!;
        var first = sheet.Questions.Single(q => q.Id == ordering).StructuredPrompt!.Items[0].Id;
        var order = JsonSerializer.Serialize(new[] { first, fault == "duplicate" ? first : Guid.NewGuid() });
        var response = fault == "mixed-payload"
            ? new PracticeResponse(ordering, null, "unexpected text")
            : new PracticeResponse(ordering, null, StructuredAnswerJson: order);
        var saved = await service.SubmitAsync(student, practice, [new(matching, null), response]);
        Assert.Equal(PracticeSubmitStatus.InvalidAnswers, saved.Status);
        Assert.False(await db.PracticeSubmissions.AnyAsync(s => s.PracticeId == practice));
    }

    private async Task<(Guid Student, Guid Practice, Guid Matching, Guid Ordering)> SeedAsync(IServiceProvider services)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var db = services.GetRequiredService<AppDbContext>();
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "M2 Practice course", Status = CourseStatus.Published };
        var module = new Module { Course = course, Title = "Module", Visibility = ModuleVisibility.Visible };
        var pairs = "ABCD".Select(key => new StructuredQuestionAuthorItem($"{key} {new string('x', 180)}",
            $"{key} {new string('y', 180)}")).ToArray();
        var matching = new PracticeQuestion { Question = MakeQuestion(QuestionType.Matching, "Match the terms", pairs) };
        var ordering = new PracticeQuestion { OrderIndex = 1, Question = MakeQuestion(QuestionType.Ordering, "Order the steps", [new("First step", null), new("Last step", null)]) };
        var practice = new Practice
        {
            Module = module, OwnerTeacherUserId = teacher.Id, Title = "M2 structured practice",
            Status = PracticeStatus.Published, Questions = [matching, ordering]
        };
        db.AddRange(practice, new Enrollment { Course = course, StudentUserId = student.Id });
        await db.SaveChangesAsync();
        return (student.Id, practice.Id, matching.Id, ordering.Id);

        Question MakeQuestion(QuestionType type, string content, IReadOnlyList<StructuredQuestionAuthorItem> items) => new()
        {
            OwnerTeacherUserId = teacher.Id, Content = content, QuestionType = type, PrimarySkill = EnglishSkill.Vocabulary,
            Explanation = "PRIVATE_ANSWER_EXPLANATION", StructuredDefinitionJson = StructuredQuestionEvaluator.CreateDefinition(type, items)
        };
    }
}
