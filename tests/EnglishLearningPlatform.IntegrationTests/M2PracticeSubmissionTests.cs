using System.Net;
using System.Text.Json;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.QuestionBank;
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
    public async Task ListeningDictation_ShowsAudioWithoutTranscript_AndGradesTypedAnswer()
    {
        const string audioUrl = "https://audio.example.test/dictation.wav";
        const string transcript = "The library opens at eight.";
        const string explanation = "PRIVATE_DICTATION_EXPLANATION";
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audio = new Stimulus
        {
            OwnerTeacherUserId = teacher.Id, StimulusType = StimulusType.Audio, ResourceUrl = audioUrl, ContentText = transcript
        };
        db.Stimuli.Add(audio);
        await db.SaveChangesAsync();
        var questions = scope.ServiceProvider.GetRequiredService<IQuestionCreationService>();
        var question = await questions.CreateAsync(teacher.Id, new()
        {
            Content = "Listen and type the sentence.", QuestionType = QuestionType.Dictation, PrimarySkill = EnglishSkill.Listening,
            Explanation = explanation, StimulusId = audio.Id, AcceptedAnswers = [transcript]
        });
        Assert.True(question.Succeeded);

        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Listening", Status = CourseStatus.Published };
        var item = new PracticeQuestion { QuestionId = question.QuestionId!.Value };
        var practice = new Practice
        {
            Module = new Module { Course = course, Title = "Dictation", Visibility = ModuleVisibility.Visible },
            OwnerTeacherUserId = teacher.Id, Title = "Listen and type", Status = PracticeStatus.Published, Questions = [item]
        };
        db.AddRange(practice, new Enrollment { Course = course, StudentUserId = student.Id });
        await db.SaveChangesAsync();

        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var takePath = $"/Practice/Take/{practice.Id}";
        var token = await IdentityTestHelpers.GetTokenAsync(client, takePath);
        var takingPage = await client.GetStringAsync(takePath);
        Assert.Contains(audioUrl, takingPage);
        Assert.Contains("Answers[0].TextAnswer", takingPage);
        Assert.DoesNotContain(transcript, takingPage);
        Assert.DoesNotContain(explanation, takingPage);
        var sessionId = await db.PracticeSessions.AsNoTracking().Where(row => row.PracticeId == practice.Id)
            .Select(row => row.Id).SingleAsync();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["SessionId"] = sessionId.ToString(),
            ["Answers[0].PracticeQuestionId"] = item.Id.ToString(),
            ["Answers[0].TextAnswer"] = "  THE library   opens at eight.  "
        });
        using var submitted = await client.PostAsync($"/Practice/Submit/{practice.Id}", form);
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        var result = (await scope.ServiceProvider.GetRequiredService<IPracticeService>().GetResultAsync(student.Id, practice.Id)).Value!;
        Assert.Equal(100m, result.Score);
        var feedback = Assert.Single(result.Questions);
        Assert.True(feedback.IsCorrect);
        Assert.Equal(transcript, Assert.Single(feedback.AcceptedAnswers));
        var resultPath = submitted.Headers.Location!.OriginalString;
        Assert.Contains(explanation, await client.GetStringAsync(resultPath));
        var mistakesPage = WebUtility.HtmlDecode(await client.GetStringAsync(resultPath + "?mistakesOnly=true"));
        Assert.DoesNotContain(explanation, mistakesPage);
        Assert.DoesNotContain("Listen and type the sentence.", mistakesPage);
        Assert.Contains("Bạn đã trả lời đúng tất cả câu hỏi.", mistakesPage);
    }

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
