using System.Text.Json;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class MixedPracticeSubmissionTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task MixedSubmission_HidesAnswers_GradesText_PreservesRetakesAndReplay()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var opened = await service.BeginAsync(data.Student, data.Practice);
        Assert.Equal(LearningAccessResult.Allowed, opened.Access);
        var sheet = opened.Value!;
        var json = JsonSerializer.Serialize(sheet);
        Assert.DoesNotContain("was living", json);
        Assert.DoesNotContain("PRIVATE_FILL_EXPLANATION", json);
        Assert.Equal(QuestionType.FillBlank, sheet.Questions.Last().QuestionType);
        var responses = new[] { new PracticeResponse(data.Mcq, data.Option), new PracticeResponse(data.Blank, null, " LIVED ") };
        Assert.Equal(PracticeSubmitStatus.Saved, (await service.SubmitSessionAsync(data.Student, data.Practice, sheet.SessionId!.Value, responses)).Status);
        var result = (await service.GetResultAsync(data.Student, data.Practice)).Value!;
        Assert.Equal(100m, result.Score);
        Assert.All(result.Questions, q => Assert.True(q.IsCorrect));
        Assert.Equal(2, result.Questions.Last().AcceptedAnswers.Count);
        Assert.True(await db.PracticeAnswers.Where(a => a.PracticeQuestionId == data.Blank).Select(a => a.IsCorrect).SingleAsync());
        var replay = responses.Select(r => r with { SelectedOptionId = null, TextAnswer = null }).ToArray();
        await service.SubmitSessionAsync(data.Student, data.Practice, sheet.SessionId.Value, replay);
        Assert.Equal(100m, (await service.GetResultAsync(data.Student, data.Practice)).Value!.Score);
        db.ChangeTracker.Clear();
        var retry = (await service.BeginAsync(data.Student, data.Practice)).Value!;
        await service.SubmitSessionAsync(data.Student, data.Practice, retry.SessionId!.Value, replay);
        var history = await db.Set<PracticeSubmissionHistory>().AsNoTracking().Include(h => h.Answers).SingleAsync(h => h.PracticeId == data.Practice);
        Assert.Equal(100m, history.Score);
        Assert.Equal(" LIVED ", history.Answers.Single(a => a.PracticeQuestionId == data.Blank).TextAnswer);
        Assert.Equal(0m, (await service.GetResultAsync(data.Student, data.Practice)).Value!.Score);
    }

    [Theory]
    [InlineData("foreign-option")]
    [InlineData("overlength")]
    [InlineData("duplicate")]
    public async Task InvalidMixedPayload_DoesNotCreateResult(string fault)
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        var responses = new[] { new PracticeResponse(data.Mcq, data.Option), new PracticeResponse(data.Blank, null, "lived") };
        if (fault == "foreign-option") responses[1] = responses[1] with { SelectedOptionId = data.Option };
        if (fault == "overlength") responses[1] = responses[1] with { TextAnswer = new string('x', 501) };
        if (fault == "duplicate") responses[1] = responses[0];
        Assert.Equal(PracticeSubmitStatus.InvalidAnswers, (await service.SubmitAsync(data.Student, data.Practice, responses)).Status);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.PracticeSubmissions.AnyAsync(s => s.PracticeId == data.Practice));
    }

    private async Task<Data> SeedAsync()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Mixed course", Status = CourseStatus.Published };
        var module = new Module { Course = course, Title = "Grammar", Visibility = ModuleVisibility.Visible };
        var option = new QuestionOption { Content = "Yesterday", IsCorrect = true };
        var mcq = new PracticeQuestion { Question = new Question { OwnerTeacherUserId = teacher.Id, Content = "Choose.", PrimarySkill = EnglishSkill.Grammar,
            Options = [option, new() { Content = "Tomorrow", OrderIndex = 1 }] } };
        var blank = new PracticeQuestion { OrderIndex = 1, Question = new Question { OwnerTeacherUserId = teacher.Id, Content = "I ___ here.", PrimarySkill = EnglishSkill.Grammar,
            QuestionType = QuestionType.FillBlank, Explanation = "PRIVATE_FILL_EXPLANATION", AcceptedAnswers = [new() { AnswerText = "lived" }, new() { AnswerText = "was living", OrderIndex = 1 }] } };
        var practice = new Practice { Module = module, OwnerTeacherUserId = teacher.Id, Title = "Mixed practice", Status = PracticeStatus.Published, Questions = [mcq, blank] };
        db.Practices.Add(practice);
        db.Enrollments.Add(new Enrollment { Course = course, StudentUserId = student.Id });
        await db.SaveChangesAsync();
        return new(student.Id, practice.Id, mcq.Id, blank.Id, option.Id);
    }
    private sealed record Data(Guid Student, Guid Practice, Guid Mcq, Guid Blank, Guid Option);
}
