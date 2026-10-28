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
            Assert.True(editor.Value!.CanEditQuestions);
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

        AssertValidation(await SaveAsync(data, Array.Empty<Guid>(), published.UpdatedAtUtc), "ít nhất một");
        AssertValidation(await WriteAsync(service => service.UnpublishAsync(
            data.Teacher.Id, data.PracticeId, InitialVersion)), "tải lại trang");
        Assert.True((await WriteAsync(service => service.UnpublishAsync(
            data.Teacher.Id, data.PracticeId, published.UpdatedAtUtc))).Success);
        var unpublished = await ReadAsync(data);
        Assert.Equal(PracticeStatus.Unpublished, unpublished.Status);
        AssertValidation(await SaveAsync(data, Array.Empty<Guid>(), unpublished.UpdatedAtUtc), "ít nhất một");

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

    [Fact]
    public async Task PublishedEdit_KeepsOpenPaperAndSubmittedResult_RetakeUsesLatestVersion()
    {
        var data = await SeedAsync();
        Assert.True((await WriteAsync(s => s.PublishAsync(data.Teacher.Id, data.PracticeId,
            null, data.ModuleId, InitialVersion))).Success);
        var finished = await EnrollStudentAsync(data);
        var working = await EnrollStudentAsync(data);
        var newcomer = await EnrollStudentAsync(data);

        var oldSheet = (await StudentAsync(s => s.BeginAsync(finished.Id, data.PracticeId))).Value!;
        Assert.Equal(PracticeSubmitStatus.Saved, (await StudentAsync(s => s.SubmitSessionAsync(
            finished.Id, data.PracticeId, oldSheet.SessionId!.Value, Answers(oldSheet, true)))).Status);
        var oldResult = (await StudentAsync(s => s.GetResultAsync(finished.Id, data.PracticeId))).Value!;
        var openSheet = (await StudentAsync(s => s.BeginAsync(working.Id, data.PracticeId))).Value!;
        var published = await ReadAsync(data);

        // Teacher bỏ một câu: chỉ phiên bản mới bị thay đổi, các liên kết cũ vẫn được giữ.
        Assert.True((await SaveAsync(data, new[] { data.SecondId }, published.UpdatedAtUtc)).Success);
        var revised = await ReadAsync(data);
        Assert.Equal(2, revised.Version);
        Assert.Equal(PracticeStatus.Published, revised.Status);
        Assert.Equal(2, revised.Questions.Count(q => q.Version == 1));
        Assert.Single(revised.Questions, q => q.Version == 2);
        using (var readScope = _factory.Services.CreateScope())
        {
            var editor = await readScope.ServiceProvider.GetRequiredService<ITeacherPracticeQuestionService>()
                .GetEditorAsync(data.Teacher.Id, data.PracticeId);
            Assert.Single(editor.Value!.Questions);
            var preview = await readScope.ServiceProvider.GetRequiredService<ITeacherPracticePreviewService>()
                .GetAsync(data.Teacher.Id, data.PracticeId);
            Assert.Single(preview.Value!.Questions);
            var listing = await readScope.ServiceProvider.GetRequiredService<ITeacherPracticeQueryService>()
                .SearchAsync(data.Teacher.Id, new TeacherPracticeSearchRequest());
            Assert.Equal(1, Assert.Single(listing.Value!.Items).QuestionCount);
        }
        var unchanged = (await StudentAsync(s => s.GetResultAsync(finished.Id, data.PracticeId))).Value!;
        Assert.Equal(JsonSerializer.Serialize(oldResult), JsonSerializer.Serialize(unchanged));

        var resumed = (await StudentAsync(s => s.BeginAsync(working.Id, data.PracticeId))).Value!;
        Assert.Equal(openSheet.SessionId, resumed.SessionId);
        Assert.Equal(1, resumed.Version);
        Assert.Equal(openSheet.Questions.Select(q => q.Id), resumed.Questions.Select(q => q.Id));
        Assert.Equal(PracticeSubmitStatus.Saved, (await StudentAsync(s => s.SubmitSessionAsync(
            working.Id, data.PracticeId, resumed.SessionId!.Value, Answers(resumed, true)))).Status);
        Assert.Equal(100m, (await StudentAsync(s => s.GetResultAsync(working.Id, data.PracticeId))).Value!.Score);

        var fresh = (await StudentAsync(s => s.BeginAsync(newcomer.Id, data.PracticeId))).Value!;
        Assert.Equal(2, fresh.Version);
        Assert.Single(fresh.Questions);
        // Không thể dùng SessionId của học viên khác để nộp bài.
        Assert.Equal(PracticeSubmitStatus.NotFound, (await StudentAsync(s => s.SubmitSessionAsync(
            newcomer.Id, data.PracticeId, openSheet.SessionId!.Value, Answers(openSheet, true)))).Status);

        var retry = (await StudentAsync(s => s.BeginAsync(finished.Id, data.PracticeId))).Value!;
        Assert.Equal(2, retry.Version);
        Assert.NotEqual(oldSheet.SessionId, retry.SessionId);
        Assert.Equal(PracticeSubmitStatus.Saved, (await StudentAsync(s => s.SubmitSessionAsync(
            finished.Id, data.PracticeId, retry.SessionId!.Value, Answers(retry, false)))).Status);
        // Gửi lại form cũ không được ghi đè kết quả của lần làm lại mới nhất.
        await StudentAsync(s => s.SubmitSessionAsync(finished.Id, data.PracticeId,
            oldSheet.SessionId!.Value, Answers(oldSheet, true)));
        var latest = (await StudentAsync(s => s.GetResultAsync(finished.Id, data.PracticeId))).Value!;
        Assert.Equal(2, latest.Version);
        Assert.Equal(0m, latest.Score);
        Assert.Single(latest.Questions);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.PracticeSubmissions.CountAsync(s => s.PracticeId == data.PracticeId
            && s.StudentUserId == finished.Id));
    }

    [Fact]
    public async Task PublishedEdit_InvalidOrStaleSelection_DoesNotChangeVersionOrHistory()
    {
        var data = await SeedAsync();
        Assert.True((await WriteAsync(s => s.PublishAsync(data.Teacher.Id, data.PracticeId,
            null, data.ModuleId, InitialVersion))).Success);
        var before = await ReadAsync(data);
        AssertValidation(await SaveAsync(data, Array.Empty<Guid>(), before.UpdatedAtUtc), "ít nhất một");
        var foreign = await SeedAsync();
        Assert.False((await SaveAsync(data, new[] { foreign.FirstId }, before.UpdatedAtUtc)).Success);
        Assert.Equal(before.Version, (await ReadAsync(data)).Version);
        Assert.True((await SaveAsync(data, new[] { data.SecondId, data.FirstId }, before.UpdatedAtUtc)).Success);
        AssertValidation(await SaveAsync(data, new[] { data.FirstId }, before.UpdatedAtUtc), "tải lại trang");
        var after = await ReadAsync(data);
        Assert.Equal(2, after.Version);
        Assert.Equal(new[] { data.SecondId, data.FirstId }, after.Questions.Where(q => q.Version == 2)
            .OrderBy(q => q.OrderIndex).Select(q => q.QuestionId));
        Assert.All(after.Questions, q => Assert.NotNull(q.Question.ContentLockedAtUtc));
    }

    [Fact]
    public async Task Http_PublishedEditorAndOpenStudentForm_KeepOldSessionAfterTeacherSave()
    {
        var data = await SeedAsync();
        Assert.True((await WriteAsync(s => s.PublishAsync(data.Teacher.Id, data.PracticeId,
            null, data.ModuleId, InitialVersion))).Success);
        var student = await EnrollStudentAsync(data);
        using var studentClient = await LoginAsync(student);
        using var teacherClient = await LoginAsync(data.Teacher);
        string takePath = "/Practice/Take/" + data.PracticeId;
        using var page = await studentClient.GetAsync(takePath);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        string html = await page.Content.ReadAsStringAsync();
        Assert.Contains("data-practice-version=\"1\"", html);
        var sheet = (await StudentAsync(s => s.BeginAsync(student.Id, data.PracticeId))).Value!;
        Assert.Contains("name=\"SessionId\" value=\"" + sheet.SessionId + "\"", html);
        string teacherVersion = await FormVersionAsync(teacherClient, data);
        var teacherToken = await IdentityTestHelpers.GetTokenAsync(teacherClient, Path(data));
        using var save = await teacherClient.PostAsync(Path(data, "Save"), new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = teacherToken,
                ["ExpectedUpdatedAtUtc"] = teacherVersion,
                ["QuestionIds"] = data.SecondId.ToString()
            }));
        save.EnsureSuccessStatusCode();
        using var saved = JsonDocument.Parse(await save.Content.ReadAsStringAsync());
        Assert.True(saved.RootElement.GetProperty("success").GetBoolean());
        using var refreshed = await studentClient.GetAsync(takePath);
        Assert.Contains("data-practice-version=\"1\"", await refreshed.Content.ReadAsStringAsync());
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await IdentityTestHelpers.GetTokenAsync(studentClient, takePath),
            ["SessionId"] = sheet.SessionId!.Value.ToString()
        };
        for (int i = 0; i < sheet.Questions.Count; i++)
        {
            fields[$"Answers[{i}].PracticeQuestionId"] = sheet.Questions[i].Id.ToString();
            fields[$"Answers[{i}].SelectedOptionId"] = sheet.Questions[i].Options[0].Id.ToString();
        }
        using var submit = await studentClient.PostAsync("/Practice/Submit/" + data.PracticeId,
            new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, submit.StatusCode);
        using var result = await studentClient.GetAsync("/Practice/Result/" + data.PracticeId);
        result.EnsureSuccessStatusCode();
        Assert.Contains("data-result-version=\"1\"", await result.Content.ReadAsStringAsync());
        using var retry = await studentClient.GetAsync(takePath);
        Assert.Contains("data-practice-version=\"2\"", await retry.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConcurrentBeginAndTeacherEdit_DoNotDuplicateSessionOrChangeItsPaper()
    {
        var data = await SeedAsync();
        Assert.True((await WriteAsync(s => s.PublishAsync(data.Teacher.Id, data.PracticeId,
            null, data.ModuleId, InitialVersion))).Success);
        var student = await EnrollStudentAsync(data);
        var sheets = await Task.WhenAll(StudentAsync(s => s.BeginAsync(student.Id, data.PracticeId)),
            StudentAsync(s => s.BeginAsync(student.Id, data.PracticeId)));
        Assert.All(sheets, s => Assert.Equal(LearningAccessResult.Allowed, s.Access));
        Assert.Equal(sheets[0].Value!.SessionId, sheets[1].Value!.SessionId);
        var old = sheets[0].Value!;
        var published = await ReadAsync(data);
        var edit = SaveAsync(data, new[] { data.SecondId }, published.UpdatedAtUtc);
        var submit = StudentAsync(s => s.SubmitSessionAsync(student.Id, data.PracticeId,
            old.SessionId!.Value, Answers(old, true)));
        await Task.WhenAll(edit, submit);
        Assert.True((await edit).Success);
        Assert.Equal(PracticeSubmitStatus.Saved, (await submit).Status);
        var result = (await StudentAsync(s => s.GetResultAsync(student.Id, data.PracticeId))).Value!;
        Assert.Equal(1, result.Version);
        Assert.Equal(100m, result.Score);
        Assert.Equal(2, result.Questions.Count);
    }

    [Fact]
    public async Task RemovedAudio_RemainsAvailableToOldSessionAndResult_ButNotNewStudent()
    {
        var data = await SeedAsync();
        string url;
        using (var scope = _factory.Services.CreateScope())
        {
            url = await scope.ServiceProvider.GetRequiredService<IStimulusAudioStore>()
                .StoreAsync(StimulusAudioTestData.Wav());
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stimulus = await db.Stimuli.SingleAsync(s => s.Id == data.StimulusId);
            stimulus.StimulusType = StimulusType.Audio;
            stimulus.ContentText = null;
            stimulus.ResourceUrl = url;
            (await db.Questions.SingleAsync(q => q.Id == data.FirstId)).PrimarySkill = EnglishSkill.Listening;
            await db.SaveChangesAsync();
        }
        Assert.True((await WriteAsync(s => s.PublishAsync(data.Teacher.Id, data.PracticeId,
            null, data.ModuleId, InitialVersion))).Success);
        var working = await EnrollStudentAsync(data);
        var newcomer = await EnrollStudentAsync(data);
        var old = (await StudentAsync(s => s.BeginAsync(working.Id, data.PracticeId))).Value!;
        Assert.True((await SaveAsync(data, new[] { data.SecondId }, (await ReadAsync(data)).UpdatedAtUtc)).Success);
        Assert.True(await CanReadAudioAsync(working.Id, url));
        Assert.False(await CanReadAudioAsync(newcomer.Id, url));
        await StudentAsync(s => s.SubmitSessionAsync(working.Id, data.PracticeId,
            old.SessionId!.Value, Answers(old, true)));
        Assert.True(await CanReadAudioAsync(working.Id, url));
        var retry = (await StudentAsync(s => s.BeginAsync(working.Id, data.PracticeId))).Value!;
        await StudentAsync(s => s.SubmitSessionAsync(working.Id, data.PracticeId,
            retry.SessionId!.Value, Answers(retry, true)));
        Assert.False(await CanReadAudioAsync(working.Id, url));
    }

    private async Task<bool> CanReadAudioAsync(Guid studentId, string url)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStimulusAudioAccessService>().CanReadAsync(studentId, url);
    }

    private async Task<ApplicationUser> EnrollStudentAsync(SeedData data)
    {
        var student = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Enrollments.Add(new Enrollment { StudentUserId = student.Id, CourseId = data.CourseId });
        await db.SaveChangesAsync();
        return student;
    }

    private async Task<T> StudentAsync<T>(Func<IPracticeService, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPracticeService>());
    }

    // Seed tạo Option đúng trước Option sai; DTO không chứa cờ đáp án đúng.
    private static PracticeResponse[] Answers(PracticeSheet sheet, bool correct) => sheet.Questions
        .Select(q => new PracticeResponse(q.Id, q.Options[correct ? 0 : 1].Id)).ToArray();

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
