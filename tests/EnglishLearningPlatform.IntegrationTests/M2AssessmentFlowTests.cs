using System.Net;
using System.Text.Json;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.AssessmentAttempts;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class M2AssessmentFlowTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task MixedAssessment_Publishes_Autosaves_RejectsInvalidBatch_AndGradesWeightedAnswers()
    {
        var data = await CreatePublishedAssessmentAsync();
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, data.Student);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        string overview = $"/Assessment/Take/{data.AssessmentId}";
        string token = await IdentityTestHelpers.GetTokenAsync(client, overview);
        using var started = await PostAsync(client, $"/Assessment/Start/{data.AssessmentId}", token, []);
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        string take = started.Headers.Location!.OriginalString;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attemptId = await db.Attempts.Where(a => a.AssessmentId == data.AssessmentId).Select(a => a.Id).SingleAsync();
        var queries = scope.ServiceProvider.GetRequiredService<IAssessmentQueryService>();
        var sheet = await queries.GetTakingSheetAsync(data.Student.Id, attemptId);
        Assert.NotNull(sheet);
        Assert.Equal(new[] { QuestionType.MultipleChoice, QuestionType.FillBlank, QuestionType.Matching,
            QuestionType.Ordering, QuestionType.MultipleChoice }, sheet.Questions.Select(q => q.QuestionType));
        string serialized = JsonSerializer.Serialize(sheet);
        Assert.DoesNotContain("StructuredDefinitionJson", serialized);
        Assert.DoesNotContain("was living", serialized);
        Assert.DoesNotContain("PRIVATE_M2_REASON", serialized);
        Assert.Null(await queries.GetAttemptResultAsync(data.Student.Id, attemptId));

        string html = await client.GetStringAsync(take);
        Assert.Contains("Answers[1].TextAnswer", html);
        Assert.Contains("Answers[2].StructuredAnswerJson", html);
        Assert.Contains("Answers[3].StructuredAnswerJson", html);
        Assert.DoesNotContain("was living", html);
        Assert.DoesNotContain("PRIVATE_M2_REASON", html);

        string answerText = "  WAS   living  ";
        using var saved = await PostAsync(client, $"/Assessment/Save/{attemptId}", token,
        [
            new("Answers.Index", "1"),
            new("Answers[1].AssessmentQuestionId", sheet.Questions[1].AssessmentQuestionId.ToString()),
            new("Answers[1].TextAnswer", answerText)
        ], jsonResponse: true);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Contains("\"saved\":true", await saved.Content.ReadAsStringAsync());
        Assert.Contains(answerText, await client.GetStringAsync(take));

        var prompt = sheet.Questions[2].StructuredPrompt!;
        Assert.Equal(2, prompt.Matches.Count);
        var matchJson = JsonSerializer.Serialize(prompt.Items.Select(item => new
        {
            leftId = item.Id,
            rightId = prompt.Matches.Single(match => match.Text == (item.Text == "cat" ? "mèo" : "chim")).Id
        }));
        var ordering = sheet.Questions[3].StructuredPrompt!;
        var duplicate = ordering.Items[0].Id;
        var invalidOrder = JsonSerializer.Serialize(new[] { duplicate, duplicate });
        using var invalid = await PostAsync(client, $"/Assessment/Save/{attemptId}", token,
            new Dictionary<string, string>
            {
                ["Answers[0].AssessmentQuestionId"] = sheet.Questions[1].AssessmentQuestionId.ToString(),
                ["Answers[0].TextAnswer"] = "wrong",
                ["Answers[1].AssessmentQuestionId"] = sheet.Questions[3].AssessmentQuestionId.ToString(),
                ["Answers[1].StructuredAnswerJson"] = invalidOrder
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(answerText, await db.AttemptAnswers.Where(answer => answer.AttemptId == attemptId)
            .Select(answer => answer.TextAnswer).SingleAsync());

        var reversedOrder = JsonSerializer.Serialize(new[] { "Goodbye", "Hello" }
            .Select(label => ordering.Items.Single(item => item.Text == label).Id));
        using var submitted = await PostAsync(client, $"/Assessment/Submit/{attemptId}", token,
            new Dictionary<string, string>
            {
                ["Answers[0].AssessmentQuestionId"] = sheet.Questions[0].AssessmentQuestionId.ToString(),
                ["Answers[0].SelectedOptionId"] = data.CorrectOptionId.ToString(),
                ["Answers[1].AssessmentQuestionId"] = sheet.Questions[2].AssessmentQuestionId.ToString(),
                ["Answers[1].StructuredAnswerJson"] = matchJson,
                ["Answers[2].AssessmentQuestionId"] = sheet.Questions[3].AssessmentQuestionId.ToString(),
                ["Answers[2].StructuredAnswerJson"] = reversedOrder
            });
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        Assert.EndsWith($"/Assessment/Result/{attemptId}", submitted.Headers.Location!.OriginalString);
        var result = await queries.GetAttemptResultAsync(data.Student.Id, attemptId);
        Assert.NotNull(result);
        Assert.Equal(47.37m, result.OverallScore);
        Assert.False(result.Passed);
        Assert.Equal(52.94m, Assert.Single(result.SkillBreakdowns).Score);
        Assert.True(Assert.Single(result.SkillBreakdowns).IsWeak);
        Assert.Equal(new[] { 3m, 5m, 0m }, result.QuestionFeedback.Select(q => q.EarnedPoints));
        var resultHtml = WebUtility.HtmlDecode(await client.GetStringAsync(submitted.Headers.Location));
        Assert.Contains(answerText, resultHtml);
        Assert.Contains("Goodbye → Hello", resultHtml);
        Assert.DoesNotContain("PRIVATE_M2_REASON", resultHtml);
        Assert.DoesNotContain("was living", resultHtml);
        Assert.DoesNotContain("lived", resultHtml);
        Assert.DoesNotContain("Hello → Goodbye", resultHtml);
        var rows = await db.AttemptAnswers.AsNoTracking().Where(answer => answer.AttemptId == attemptId).ToListAsync();
        Assert.Equal(4, rows.Count);
        Assert.Equal(3, rows.Count(row => row.IsCorrect == true));
        Assert.Single(rows, row => row.SelectedOptionId.HasValue);
        Assert.Equal(2, rows.Count(row => row.StructuredAnswerJson is not null));

        using var replay = await PostAsync(client, $"/Assessment/Submit/{attemptId}", token, []);
        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        Assert.Equal(result.FinalizedAtUtc, (await queries.GetAttemptResultAsync(data.Student.Id, attemptId))!.FinalizedAtUtc);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        Assert.Null(await queries.GetAttemptResultAsync(other.Id, attemptId));
    }

    [Fact]
    public async Task AtDeadline_SavesNoLateTypedAnswer_AndGradesPreviouslySavedTypedAnswer()
    {
        var data = await CreatePublishedAssessmentAsync();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var attempts = new AssessmentAttemptService(db, services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<IExamScoringService>(), services.GetRequiredService<IAssessmentDefinitionValidator>(), clock);
        var started = await attempts.StartAsync(data.Student.Id, data.AssessmentId);
        Assert.Equal(AttemptStartStatus.Started, started.Status);
        Guid attemptId = started.AttemptId!.Value;
        Guid blank = await db.AssessmentQuestions.Where(link => link.AssessmentId == data.AssessmentId && link.OrderIndex == 1)
            .Select(link => link.Id).SingleAsync();
        Assert.Equal(AttemptSaveStatus.Saved, await attempts.SaveResponsesAsync(data.Student.Id, attemptId,
            [new(blank, null, "lived")]));
        clock.UtcNow = started.DeadlineUtc!.Value;
        Assert.Equal(AttemptSaveStatus.Expired, await attempts.SaveResponsesAsync(data.Student.Id, attemptId,
            [new(blank, null, "wrong")]));

        var saved = await db.Attempts.AsNoTracking().Include(attempt => attempt.Answers).SingleAsync(attempt => attempt.Id == attemptId);
        Assert.Equal(AttemptFinalizationReason.DeadlineElapsed, saved.FinalizationReason);
        Assert.Equal(15.79m, saved.OverallScore);
        var answer = Assert.Single(saved.Answers);
        Assert.Equal("lived", answer.TextAnswer);
        Assert.True(answer.IsCorrect);
    }

    private async Task<Fixture> CreatePublishedAssessmentAsync()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var mcq = new Question
        {
            OwnerTeacherUserId = teacher.Id,
            Content = "Choose the greeting.",
            PrimarySkill = EnglishSkill.Grammar,
            Options = [new() { Content = "Hi", IsCorrect = true }, new() { Content = "Later", OrderIndex = 1 }]
        };
        var fill = new Question
        {
            OwnerTeacherUserId = teacher.Id,
            Content = "I ____ in Hanoi then.",
            PrimarySkill = EnglishSkill.Grammar,
            QuestionType = QuestionType.FillBlank,
            Explanation = "PRIVATE_M2_REASON",
            AcceptedAnswers = [new() { AnswerText = "lived" }, new() { AnswerText = "was living", OrderIndex = 1 }]
        };
        Question Structured(QuestionType type, string content, IReadOnlyList<StructuredQuestionAuthorItem> items) => new()
        {
            OwnerTeacherUserId = teacher.Id, Content = content, PrimarySkill = EnglishSkill.Grammar,
            QuestionType = type, StructuredDefinitionJson = StructuredQuestionEvaluator.CreateDefinition(type, items)
        };
        var matching = Structured(QuestionType.Matching, "Match the animals.", [new("cat", "mèo"), new("bird", "chim")]);
        var ordering = Structured(QuestionType.Ordering, "Order the conversation.", [new("Hello"), new("Goodbye")]);
        var extra = new Question
        {
            OwnerTeacherUserId = teacher.Id,
            Content = "Pick a color.",
            PrimarySkill = EnglishSkill.Vocabulary,
            Options = [new() { Content = "Blue", IsCorrect = true }, new() { Content = "House", OrderIndex = 1 }]
        };
        var assessment = new Assessment
        {
            OwnerTeacherUserId = teacher.Id, Title = "M2 weighted assessment", AssessmentType = AssessmentType.PracticeExam,
            DurationMinutes = 10, MaxAttempts = 2, PassingScore = 50m, Status = AssessmentStatus.Draft
        };
        db.Questions.AddRange(mcq, fill, matching, ordering, extra);
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();
        var saved = await services.GetRequiredService<ITeacherAssessmentQuestionService>().SaveAsync(teacher.Id, assessment.Id,
            new UpdateAssessmentQuestionsRequest
            {
                ExpectedUpdatedAtUtc = assessment.UpdatedAtUtc,
                Items =
                [
                    new() { QuestionId = mcq.Id, Points = 1m }, new() { QuestionId = fill.Id, Points = 3m },
                    new() { QuestionId = matching.Id, Points = 5m }, new() { QuestionId = ordering.Id, Points = 8m },
                    new() { QuestionId = extra.Id, Points = 2m }
                ]
            });
        Assert.True(saved.Success);
        var version = await db.Assessments.AsNoTracking().Where(a => a.Id == assessment.Id).Select(a => a.UpdatedAtUtc).SingleAsync();
        var published = await services.GetRequiredService<ITeacherAssessmentPublishService>().PublishAsync(teacher.Id, assessment.Id, version);
        Assert.True(published.Success);
        return new(student, assessment.Id, mcq.Options.Single(option => option.IsCorrect).Id);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string token,
        IEnumerable<KeyValuePair<string, string>> fields, bool jsonResponse = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(fields.Append(new("__RequestVerificationToken", token)))
        };
        if (jsonResponse)
            request.Headers.Accept.ParseAdd("application/json");
        return await client.SendAsync(request);
    }

    private sealed record Fixture(ApplicationUser Student, Guid AssessmentId, Guid CorrectOptionId);

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
