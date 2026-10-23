using System.Net;
using System.Text.Json;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Chỉ bù phần Preview mới; các rule Publish/chấm bài đã có test riêng.
public sealed class TeacherPracticePreviewTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData(PracticeStatus.Draft)]
    [InlineData(PracticeStatus.Published)]
    public async Task RepeatedPreview_ShowsOrderedPassageAudio_WithoutChangingContentOrResults(PracticeStatus status)
    {
        var data = await Seed(status);
        string before = await Snapshot(data.Id);
        using var client = await Login(data.Teacher);
        for (int repeat = 0; repeat < 3; repeat++)
        {
            using var response = await client.GetAsync(Path(data.Id));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            string html = await response.Content.ReadAsStringAsync();
            Assert.Contains("QA5_PASSAGE", html);
            Assert.Contains(data.AudioUrl, html);
            Assert.Contains("&lt;script&gt;", html);
            Assert.DoesNotContain("<script>PRIVATE_QUESTION", html);
            Assert.DoesNotContain("PRIVATE_EXPLANATION", html);
            Assert.DoesNotContain("data-practice-form", html);
            Assert.DoesNotContain("/Practice/Submit", html);
            Assert.DoesNotContain("name=\"__RequestVerificationToken\"", html.Split("Preview Practice</h1>").Last().Split("</main>").First());
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var result = await scope.ServiceProvider.GetRequiredService<ITeacherPracticePreviewService>().GetAsync(data.Teacher.Id, data.Id);
            Assert.Equal(LearningAccessResult.Allowed, result.Access);
            Assert.Equal(new[] { EnglishSkill.Reading, EnglishSkill.Listening }, result.Value!.Questions.Select(q => q.Skill));
            Assert.All(result.Value.Questions, q => Assert.Equal(new[] { "Wrong", "Right" }, q.Options.Select(o => o.Content)));
            Assert.DoesNotContain("IsCorrect", JsonSerializer.Serialize(result.Value));
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Equal(before, await Snapshot(data.Id));
        }
    }

    [Fact]
    public async Task DirectRequests_RequireOwnerTeacherAndGet_WithoutWrites()
    {
        var data = await Seed(PracticeStatus.Draft);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        string before = await Snapshot(data.Id);
        using var guest = IdentityTestHelpers.CreateClient(factory);
        using var anonymous = await guest.GetAsync(Path(data.Id));
        Assert.Contains("/Account/Login", anonymous.Headers.Location!.ToString());
        using var foreign = await Login(other);
        using var denied = await foreign.GetAsync(Path(data.Id));
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var studentClient = await Login(student);
        using var forbidden = await studentClient.GetAsync(Path(data.Id));
        Assert.Contains("/Account/AccessDenied", forbidden.Headers.Location!.ToString());
        using var owner = await Login(data.Teacher);
        using var post = await owner.PostAsync(Path(data.Id), new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeacherPracticePreviewService>();
        Assert.Equal(LearningAccessResult.Forbidden, (await service.GetAsync(student.Id, data.Id)).Access);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Id == data.Teacher.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.AccountStatus, AccountStatus.Disabled));
        Assert.Equal(LearningAccessResult.Forbidden, (await service.GetAsync(data.Teacher.Id, data.Id)).Access);
        Assert.Equal(before, await Snapshot(data.Id));
    }

    [Theory]
    [InlineData("Question")]
    [InlineData("Stimulus")]
    [InlineData("Course")]
    public async Task CorruptForeignNestedOwner_IsNotExposed(string resource)
    {
        var data = await Seed(PracticeStatus.Draft);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Dữ liệu hỏng chỉ dựng trong fixture test, không phải thao tác UAT trên DB demo.
        if (resource == "Question") await db.Questions.Where(q => q.OwnerTeacherUserId == data.Teacher.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.OwnerTeacherUserId, other.Id));
        if (resource == "Stimulus") await db.Stimuli.Where(q => q.OwnerTeacherUserId == data.Teacher.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.OwnerTeacherUserId, other.Id));
        if (resource == "Course") await db.Courses.Where(q => q.OwnerTeacherUserId == data.Teacher.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.OwnerTeacherUserId, other.Id));
        string before = await Snapshot(data.Id);
        using var client = await Login(data.Teacher);
        using var response = await client.GetAsync(Path(data.Id));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("QA5_PASSAGE", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await Snapshot(data.Id));
    }

    [Fact]
    public async Task EmptyDraft_ShowsMissingContentWithoutPublishing()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var practice = new Practice { OwnerTeacherUserId = teacher.Id, Title = "Empty preview" };
        db.Practices.Add(practice);
        await db.SaveChangesAsync();
        var result = await scope.ServiceProvider.GetRequiredService<ITeacherPracticePreviewService>().GetAsync(teacher.Id, practice.Id);
        Assert.Equal(LearningAccessResult.Allowed, result.Access);
        Assert.Empty(result.Value!.Questions);
        Assert.Equal(2, result.Value.Warnings.Count);
        Assert.Equal(PracticeStatus.Draft, await db.Practices.Where(p => p.Id == practice.Id).Select(p => p.Status).SingleAsync());
    }

    private static string Path(Guid id) => "/TeacherPracticePreview/Index/" + id;
    private async Task<HttpClient> Login(ApplicationUser user)
    {
        var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private async Task<string> Snapshot(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var value = await db.Practices.AsNoTracking().Where(p => p.Id == id).Select(p => new
        {
            p.Id, p.Title, p.Description, p.Status, p.LessonId, p.ModuleId, p.UpdatedAtUtc, p.CreatedAtUtc,
            Questions = p.Questions.OrderBy(q => q.Id).Select(q => new
            {
                q.Id, q.OrderIndex, q.QuestionId, q.Question.Content, q.Question.Explanation,
                q.Question.PrimarySkill, q.Question.StimulusId, q.Question.ContentLockedAtUtc,
                Options = q.Question.Options.OrderBy(o => o.Id).Select(o => new { o.Id, o.Content, o.IsCorrect, o.OrderIndex }),
                Stimulus = q.Question.Stimulus == null ? null : new { q.Question.Stimulus.ContentText,
                    q.Question.Stimulus.ResourceUrl, q.Question.Stimulus.ContentLockedAtUtc }
            }),
            Results = p.Submissions.OrderBy(s => s.Id).Select(s => new { s.Id, s.Score, s.SubmittedAtUtc,
                Answers = s.Answers.OrderBy(a => a.Id).Select(a => new { a.Id, a.SelectedOptionId, a.PracticeQuestionId }) })
        }).SingleAsync();
        return JsonSerializer.Serialize(new { value, Attempts = await db.Attempts.CountAsync(),
            Results = await db.AttemptSkillResults.CountAsync(), PracticeResults = await db.PracticeSubmissions.CountAsync() });
    }

    private async Task<(ApplicationUser Teacher, Guid Id, string AudioUrl)> Seed(PracticeStatus status)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audio = await scope.ServiceProvider.GetRequiredService<IStimulusCreationService>().CreateAsync(teacher.Id,
            new CreateStimulusRequest { StimulusType = StimulusType.Audio, AudioContent = StimulusAudioTestData.Wav() });
        Assert.True(audio.Success);
        var audioStimulus = await db.Stimuli.SingleAsync(s => s.Id == audio.Id);
        var practice = new Practice { OwnerTeacherUserId = teacher.Id, Title = "Preview practice", Description = "Instructions",
            Status = status, Lesson = new Lesson { Title = "Lesson", Status = LessonStatus.Published,
                Module = new Module { Title = "Module", Course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Course" } } } };
        for (int index = 1; index >= 0; index--)
            practice.Questions.Add(new PracticeQuestion { OrderIndex = index, Question = new Question
            {
                OwnerTeacherUserId = teacher.Id, Content = "<script>PRIVATE_QUESTION</script> " + index,
                Explanation = "PRIVATE_EXPLANATION", PrimarySkill = index == 0 ? EnglishSkill.Reading : EnglishSkill.Listening,
                Stimulus = index == 0 ? new Stimulus { OwnerTeacherUserId = teacher.Id, StimulusType = StimulusType.Passage,
                    ContentText = "QA5_PASSAGE" } : audioStimulus,
                Options = [new QuestionOption { Content = "Right", IsCorrect = true, OrderIndex = 1 }, new QuestionOption { Content = "Wrong", OrderIndex = 0 }]
            } });
        db.Practices.Add(practice);
        if (status == PracticeStatus.Published)
            db.PracticeSubmissions.Add(new PracticeSubmission { Practice = practice, StudentUserId = student.Id, Score = 50m,
                Answers = [new PracticeAnswer { PracticeQuestion = practice.Questions.First(), SelectedOptionId = practice.Questions.First().Question.Options.First().Id }] });
        await db.SaveChangesAsync();
        return (teacher, practice.Id, audioStimulus.ResourceUrl!);
    }
}
