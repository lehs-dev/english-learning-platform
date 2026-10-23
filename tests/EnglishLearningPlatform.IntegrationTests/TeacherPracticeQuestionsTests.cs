using System.Net;
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

// Dùng SQL Server test riêng của factory, không đọc/ghi database demo.
// Tập trung vào lưu đề và xuất bản; không lặp lại toàn bộ test validator đã có.
public sealed class TeacherPracticeQuestionsTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly DateTimeOffset InitialVersion =
        new DateTimeOffset(2026, 10, 18, 1, 0, 0, TimeSpan.Zero);
    private readonly IntegrationTestFactory _factory;

    public TeacherPracticeQuestionsTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Save_ReordersRemovesClearsAndRejectsStalePage_WithoutDeletingQuestions()
    {
        var data = await SeedAsync();
        Guid[] reversed = { data.SecondId, data.FirstId };

        var result = await SaveAsync(data, reversed, InitialVersion);

        Assert.True(result.Success);
        var saved = await ReadAsync(data);
        Assert.Equal(reversed, saved.Questions.OrderBy(q => q.OrderIndex).Select(q => q.QuestionId));
        Assert.Equal(new[] { 0, 1 }, saved.Questions.OrderBy(q => q.OrderIndex).Select(q => q.OrderIndex));
        Assert.NotEqual(InitialVersion, saved.UpdatedAtUtc);

        // Tab cũ không được ghi đè kết quả vừa lưu ở tab khác.
        var stale = await SaveAsync(data, new[] { data.FirstId }, InitialVersion);
        AssertValidation(stale, "tải lại trang");
        var afterStale = await ReadAsync(data);
        Assert.Equal(saved.UpdatedAtUtc, afterStale.UpdatedAtUtc);
        Assert.Equal(reversed, afterStale.Questions.OrderBy(q => q.OrderIndex).Select(q => q.QuestionId));

        Assert.True((await SaveAsync(data, new[] { data.FirstId }, saved.UpdatedAtUtc)).Success);
        var reduced = await ReadAsync(data);
        Assert.Equal(data.FirstId, Assert.Single(reduced.Questions).QuestionId);
        Assert.True((await SaveAsync(data, Array.Empty<Guid>(), reduced.UpdatedAtUtc)).Success);
        Assert.Empty((await ReadAsync(data)).Questions);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Questions.CountAsync(q => q.OwnerTeacherUserId == data.Teacher.Id));
        Assert.Equal(4, await db.QuestionOptions.CountAsync(q => q.Question.OwnerTeacherUserId == data.Teacher.Id));
    }

    [Theory]
    [InlineData("Duplicate", "trùng")]
    [InlineData("EmptyId", "ID câu hỏi rỗng")]
    [InlineData("Missing", "Không tìm thấy")]
    [InlineData("Foreign", "Không tìm thấy")]
    [InlineData("Archived", "Archived")]
    public async Task Save_InvalidSelection_DoesNotReplaceExistingLinks(string fault, string message)
    {
        var data = await SeedAsync();
        Guid[] ids = { data.FirstId, data.SecondId };
        if (fault == "Duplicate") ids = new[] { data.FirstId, data.FirstId };
        if (fault == "EmptyId") ids = new[] { Guid.Empty };
        if (fault == "Missing") ids = new[] { Guid.NewGuid() };
        if (fault == "Foreign") ids = new[] { (await SeedAsync()).FirstId };
        if (fault == "Archived")
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Questions.Where(q => q.Id == data.SecondId)
                .ExecuteUpdateAsync(set => set.SetProperty(q => q.Status, QuestionStatus.Archived));
        }
        var before = await ReadAsync(data);

        var result = await SaveAsync(data, ids, InitialVersion);

        AssertValidation(result, message);
        var after = await ReadAsync(data);
        Assert.Equal(before.Questions.OrderBy(q => q.Id).Select(q => q.Id),
            after.Questions.OrderBy(q => q.Id).Select(q => q.Id));
        Assert.Equal(InitialVersion, after.UpdatedAtUtc);
        Assert.Equal(PracticeStatus.Draft, after.Status);
    }

    [Theory]
    [InlineData("None", "đúng một")]
    [InlineData("Both", "đúng một")]
    [InlineData("ForeignParent", "không thuộc")]
    [InlineData("ArchivedCourse", "Course đã Archived")]
    [InlineData("Empty", "ít nhất một câu hỏi")]
    [InlineData("InvalidMcq", "đúng một đáp án")]
    [InlineData("BlankPassage", "passage không được để trống")]
    [InlineData("InvalidAudio", "URL audio")]
    [InlineData("ArchivedQuestion", "Archived")]
    public async Task Publish_InvalidDefinition_DoesNotPublishOrLockAnyContent(string fault, string message)
    {
        var data = await SeedAsync();
        Guid? lessonId = data.LessonId;
        Guid? moduleId = null;
        if (fault == "None") lessonId = null;
        if (fault == "Both") moduleId = data.ModuleId;
        if (fault == "ForeignParent") lessonId = (await SeedAsync()).LessonId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (fault == "ArchivedCourse")
                await db.Courses.Where(c => c.Id == data.CourseId)
                    .ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, CourseStatus.Archived));
            if (fault == "Empty")
                await db.PracticeQuestions.Where(q => q.PracticeId == data.PracticeId).ExecuteDeleteAsync();
            if (fault == "InvalidMcq")
                await db.QuestionOptions.Where(q => q.QuestionId == data.SecondId)
                    .ExecuteUpdateAsync(set => set.SetProperty(q => q.IsCorrect, false));
            if (fault == "BlankPassage")
                await db.Stimuli.Where(s => s.Id == data.StimulusId)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.ContentText, "   "));
            if (fault == "InvalidAudio")
            {
                await db.Stimuli.Where(s => s.Id == data.StimulusId).ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.StimulusType, StimulusType.Audio)
                    .SetProperty(s => s.ResourceUrl, "javascript:alert(1)"));
                await db.Questions.Where(q => q.Id == data.FirstId)
                    .ExecuteUpdateAsync(set => set.SetProperty(q => q.PrimarySkill, EnglishSkill.Listening));
            }
            if (fault == "ArchivedQuestion")
                await db.Questions.Where(q => q.Id == data.SecondId)
                    .ExecuteUpdateAsync(set => set.SetProperty(q => q.Status, QuestionStatus.Archived));
        }
        var before = await ReadAsync(data);

        var result = await WriteAsync(service => service.PublishAsync(
            data.Teacher.Id, data.PracticeId, lessonId, moduleId, InitialVersion));

        AssertValidation(result, message);
        var after = await ReadAsync(data);
        Assert.Equal(PracticeStatus.Draft, after.Status);
        Assert.Equal(InitialVersion, after.UpdatedAtUtc);
        Assert.Null(after.LessonId);
        Assert.Null(after.ModuleId);
        Assert.Equal(before.Questions.OrderBy(q => q.Id).Select(q => q.Id),
            after.Questions.OrderBy(q => q.Id).Select(q => q.Id));
        // Kể cả câu đầu hợp lệ, câu sau lỗi thì không được khóa một phần đề.
        Assert.All(after.Questions, item => Assert.Null(item.Question.ContentLockedAtUtc));
        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(await checkDb.Stimuli.Where(s => s.Id == data.StimulusId)
            .Select(s => s.ContentLockedAtUtc).SingleAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_Unpublish_Republish_PreservesDefinitionLocksAndStudentResult(bool moduleParent)
    {
        var data = await SeedAsync();
        Guid? lessonId = moduleParent ? null : data.LessonId;
        Guid? moduleId = moduleParent ? data.ModuleId : null;
        var original = await ReadAsync(data);

        Assert.True((await WriteAsync(service => service.PublishAsync(
            data.Teacher.Id, data.PracticeId, lessonId, moduleId, InitialVersion))).Success);
        var published = await ReadAsync(data);
        Assert.Equal(PracticeStatus.Published, published.Status);
        Assert.Equal(lessonId, published.LessonId);
        Assert.Equal(moduleId, published.ModuleId);
        Assert.All(published.Questions, item =>
            Assert.Equal(published.UpdatedAtUtc, item.Question.ContentLockedAtUtc));
        Assert.Equal(published.UpdatedAtUtc,
            published.Questions.Single(q => q.QuestionId == data.FirstId).Question.Stimulus!.ContentLockedAtUtc);

        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ITeacherPracticeQuestionService>();
            var editor = await service.GetEditorAsync(data.Teacher.Id, data.PracticeId);
            Assert.Equal(LearningAccessResult.Allowed, editor.Access);
            Assert.False(editor.Value!.CanEditQuestions);
        }

        // Có kết quả cũ để kiểm tra Unpublish không xóa submission/answer.
        var student = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);
        Guid answerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = published.Questions.First();
            var submission = new PracticeSubmission
            {
                StudentUserId = student.Id, PracticeId = data.PracticeId, Score = 50
            };
            var answer = new PracticeAnswer
            {
                PracticeSubmission = submission, PracticeQuestionId = item.Id,
                SelectedOptionId = item.Question.Options.First().Id
            };
            answerId = answer.Id;
            db.PracticeAnswers.Add(answer);
            await db.SaveChangesAsync();
        }

        AssertValidation(await SaveAsync(data, Array.Empty<Guid>(), published.UpdatedAtUtc), "Draft");
        AssertValidation(await WriteAsync(service => service.UnpublishAsync(
            data.Teacher.Id, data.PracticeId, InitialVersion)), "tải lại trang");
        Assert.True((await WriteAsync(service => service.UnpublishAsync(
            data.Teacher.Id, data.PracticeId, published.UpdatedAtUtc))).Success);
        var unpublished = await ReadAsync(data);
        Assert.Equal(PracticeStatus.Unpublished, unpublished.Status);
        AssertValidation(await SaveAsync(data, Array.Empty<Guid>(), unpublished.UpdatedAtUtc), "Draft");

        // Đã Publish thì không được đổi sang parent khác, kể cả cùng Teacher.
        AssertValidation(await WriteAsync(service => service.PublishAsync(
            data.Teacher.Id, data.PracticeId,
            moduleParent ? data.LessonId : null,
            moduleParent ? null : data.ModuleId,
            unpublished.UpdatedAtUtc)), "không được đổi");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Questions.Where(q => q.Id == data.FirstId)
                .ExecuteUpdateAsync(set => set.SetProperty(q => q.Status, QuestionStatus.Archived));
        }
        // Archived được dùng lại trong đề cũ, không được coi như chọn mới.
        Assert.True((await WriteAsync(service => service.PublishAsync(
            data.Teacher.Id, data.PracticeId, lessonId, moduleId, unpublished.UpdatedAtUtc))).Success);
        var republished = await ReadAsync(data);
        Assert.Equal(PracticeStatus.Published, republished.Status);
        Assert.Equal(original.Questions.OrderBy(q => q.OrderIndex).Select(q => q.Id),
            republished.Questions.OrderBy(q => q.OrderIndex).Select(q => q.Id));
        Assert.All(republished.Questions, item =>
            Assert.Equal(published.UpdatedAtUtc, item.Question.ContentLockedAtUtc));
        Assert.Equal(published.UpdatedAtUtc,
            republished.Questions.Single(q => q.QuestionId == data.FirstId).Question.Stimulus!.ContentLockedAtUtc);
        var retained = Assert.Single(republished.Submissions);
        Assert.Equal(50m, retained.Score);
        Assert.Equal(answerId, Assert.Single(retained.Answers).Id);
    }

    [Theory]
    [InlineData(AppRoles.Teacher)]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Admin)]
    public async Task Service_ForeignOrNonTeacher_CannotReadOrWritePractice(string role)
    {
        var data = await SeedAsync();
        var actor = await IdentityTestHelpers.CreateUserAsync(_factory, role);
        var expected = role == AppRoles.Teacher
            ? LearningAccessResult.NotFound : LearningAccessResult.Forbidden;
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeacherPracticeQuestionService>();

        Assert.Equal(expected, (await service.GetEditorAsync(actor.Id, data.PracticeId)).Access);
        Assert.Equal(expected, (await service.UpdateQuestionsAsync(actor.Id, data.PracticeId,
            new UpdatePracticeQuestionsRequest { ExpectedUpdatedAtUtc = InitialVersion })).Access);
        Assert.Equal(expected, (await service.PublishAsync(actor.Id, data.PracticeId,
            data.LessonId, null, InitialVersion)).Access);
        Assert.Equal(expected, (await service.UnpublishAsync(actor.Id, data.PracticeId, InitialVersion)).Access);
        Assert.Equal(InitialVersion, (await ReadAsync(data)).UpdatedAtUtc);
    }

    [Theory]
    [InlineData("Save")]
    [InlineData("Publish")]
    [InlineData("Unpublish")]
    public async Task Http_PostWithoutAntiforgeryToken_IsRejectedWithoutChanges(string action)
    {
        var data = await SeedAsync();
        using var client = await LoginAsync(data.Teacher);

        using var response = await client.PostAsync(Path(data, action),
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(InitialVersion, (await ReadAsync(data)).UpdatedAtUtc);
    }

    [Fact]
    public async Task Http_EditorSavePublishUnpublish_UsesRealFormsAndRoundTripsVersion()
    {
        var data = await SeedAsync();
        var foreign = await SeedAsync();
        using var client = await LoginAsync(data.Teacher);
        using var page = await client.GetAsync(Path(data));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl!.NoStore);
        string html = await page.Content.ReadAsStringAsync();
        Assert.Contains("data-selected-question-list", html);
        Assert.Contains("data-publication-form", html);
        Assert.Contains("/js/teacher-practice-questions.js", html);
        Assert.Contains(data.LessonId.ToString(), html);
        Assert.Contains(data.ModuleId.ToString(), html);
        Assert.DoesNotContain(foreign.FirstId.ToString(), html);
        Assert.DoesNotContain(foreign.LessonId.ToString(), html);

        var token = await IdentityTestHelpers.GetTokenAsync(client, Path(data));
        // Hai input cùng tên phải được MVC bind đúng thứ tự, không chỉ thử service.
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("ExpectedUpdatedAtUtc", InitialVersion.ToString("O")),
            new("QuestionIds", data.SecondId.ToString()),
            new("QuestionIds", data.FirstId.ToString())
        };
        using var save = await client.PostAsync(Path(data, "Save"), new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        using var json = JsonDocument.Parse(await save.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        var saved = await ReadAsync(data);
        Assert.Equal(data.SecondId, saved.Questions.Single(q => q.OrderIndex == 0).QuestionId);

        // Token phiên bản lấy từ form Razor thật để bắt lỗi định dạng ngày giờ.
        string version = await FormVersionAsync(client, data);
        var publishFields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["expectedUpdatedAtUtc"] = version,
            ["lessonId"] = data.LessonId.ToString()
        };
        using var publish = await client.PostAsync(Path(data, "Publish"), new FormUrlEncodedContent(publishFields));
        Assert.Equal(HttpStatusCode.Redirect, publish.StatusCode);
        Assert.Equal(PracticeStatus.Published, (await ReadAsync(data)).Status);

        publishFields["expectedUpdatedAtUtc"] = await FormVersionAsync(client, data);
        using var unpublish = await client.PostAsync(Path(data, "Unpublish"), new FormUrlEncodedContent(publishFields));
        Assert.Equal(HttpStatusCode.Redirect, unpublish.StatusCode);
        Assert.Equal(PracticeStatus.Unpublished, (await ReadAsync(data)).Status);
    }

    // Scope mới cho mỗi thao tác: tránh test vô tình dùng entity cache của EF.
    private async Task<WriteOutcome> WriteAsync(Func<ITeacherPracticeQuestionService, Task<WriteOutcome>> action)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeacherPracticeQuestionService>();
        return await action(service);
    }

    private Task<WriteOutcome> SaveAsync(SeedData data, Guid[] ids, DateTimeOffset? version)
    {
        return WriteAsync(service => service.UpdateQuestionsAsync(data.Teacher.Id, data.PracticeId,
            new UpdatePracticeQuestionsRequest
            {
                ExpectedUpdatedAtUtc = version, QuestionIds = ids.ToList()
            }));
    }

    private async Task<Practice> ReadAsync(SeedData data)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Practices.AsNoTracking().AsSplitQuery()
            .Include(p => p.Questions).ThenInclude(q => q.Question).ThenInclude(q => q.Options)
            .Include(p => p.Questions).ThenInclude(q => q.Question).ThenInclude(q => q.Stimulus)
            .Include(p => p.Submissions).ThenInclude(s => s.Answers)
            .SingleAsync(p => p.Id == data.PracticeId);
    }

    // Kiểm tra đúng lỗi nghiệp vụ, không chấp nhận lỗi exception chung làm test pass.
    private static void AssertValidation(WriteOutcome result, string message)
    {
        Assert.Equal(LearningAccessResult.Allowed, result.Access);
        Assert.False(result.Success);
        Assert.Contains(result.Errors.SelectMany(error => error.Value),
            error => error.Contains(message, StringComparison.OrdinalIgnoreCase));
    }

    private static string Path(SeedData data, string action = "Index")
    {
        return "/TeacherPracticeQuestions/" + action + "/" + data.PracticeId;
    }

    private async Task<HttpClient> LoginAsync(ApplicationUser user)
    {
        var client = IdentityTestHelpers.CreateClient(_factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    private static async Task<string> FormVersionAsync(HttpClient client, SeedData data)
    {
        using var response = await client.GetAsync(Path(data));
        response.EnsureSuccessStatusCode();
        string html = await response.Content.ReadAsStringAsync();
        var match = System.Text.RegularExpressions.Regex.Match(html,
            "name=\"ExpectedUpdatedAtUtc\"\\s+value=\"([^\"]+)\"");
        Assert.True(match.Success, "Form phải mang phiên bản Practice.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task<SeedData> SeedAsync()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new Course
        {
            OwnerTeacherUserId = teacher.Id, Title = "Test course", Status = CourseStatus.Published
        };
        var module = new Module { Course = course, Title = "Test module", Visibility = ModuleVisibility.Visible };
        var lesson = new Lesson { Module = module, Title = "Test lesson", Status = LessonStatus.Published };
        var stimulus = new Stimulus
        {
            OwnerTeacherUserId = teacher.Id, Title = "Test passage",
            StimulusType = StimulusType.Passage, ContentText = "A short passage for testing."
        };
        var first = NewQuestion(teacher.Id, "First question");
        first.Stimulus = stimulus;
        var second = NewQuestion(teacher.Id, "Second question");
        var practice = new Practice
        {
            OwnerTeacherUserId = teacher.Id, Title = "Test draft", UpdatedAtUtc = InitialVersion,
            Questions = new List<PracticeQuestion>
            {
                new PracticeQuestion { Question = first, OrderIndex = 0 },
                new PracticeQuestion { Question = second, OrderIndex = 1 }
            }
        };
        db.Lessons.Add(lesson);
        db.Practices.Add(practice);
        await db.SaveChangesAsync();
        return new SeedData(teacher, course.Id, module.Id, lesson.Id,
            practice.Id, first.Id, second.Id, stimulus.Id);
    }

    private static Question NewQuestion(Guid ownerId, string content)
    {
        return new Question
        {
            OwnerTeacherUserId = ownerId, Content = content, PrimarySkill = EnglishSkill.Reading,
            Options = new List<QuestionOption>
            {
                new QuestionOption { Content = "Correct", IsCorrect = true, OrderIndex = 0 },
                new QuestionOption { Content = "Wrong", IsCorrect = false, OrderIndex = 1 }
            }
        };
    }

    private sealed record SeedData(ApplicationUser Teacher, Guid CourseId, Guid ModuleId,
        Guid LessonId, Guid PracticeId, Guid FirstId, Guid SecondId, Guid StimulusId);
}
