using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.AssessmentAuthoring;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

using AssessmentEntity = EnglishLearningPlatform.Domain.Entities.Assessment;

namespace EnglishLearningPlatform.IntegrationTests;

// SQL Server riêng của IntegrationTestFactory, không sửa database demo của người dùng.
public sealed class TeacherAssessmentConfigurationTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly DateTimeOffset Version =
        new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OldLock = Version.AddDays(-1);
    private readonly IntegrationTestFactory _factory;

    public TeacherAssessmentConfigurationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_ValidPublishedConfiguration_PreservesContentScopeStatusAndOldLocks(bool final)
    {
        var data = await SeedAsync(final);
        var before = await ReadAsync(data);
        var input = await InputAsync(data);
        input.Title = "  Updated configuration  ";
        input.Description = "  Saved description  ";
        input.AssessmentType = AssessmentType.PracticeExam;
        input.TargetSkill = null;
        input.DurationMinutes = 37;
        input.MaxAttempts = 4;
        input.PassingScore = 81.25m;

        Assert.True((await SaveAsync(data, input)).Success);

        var after = await ReadAsync(data);
        Assert.Equal("Updated configuration", after.Title);
        Assert.Equal("Saved description", after.Description);
        Assert.Equal(AssessmentType.PracticeExam, after.AssessmentType);
        Assert.Null(after.TargetSkill);
        Assert.Equal(37, after.DurationMinutes);
        Assert.Equal(4, after.MaxAttempts);
        Assert.Equal(81.25m, after.PassingScore);
        Assert.Equal(AssessmentStatus.Published, after.Status);
        Assert.Equal(before.OwnerTeacherUserId, after.OwnerTeacherUserId);
        Assert.Equal(before.CourseId, after.CourseId);
        Assert.NotEqual(Version, after.UpdatedAtUtc);
        Assert.Equal(Content(before), Content(after));
        Assert.Empty(after.Attempts);
        foreach (var item in after.Questions)
        {
            Assert.Equal(item.OrderIndex == 0 ? OldLock : after.UpdatedAtUtc, item.Question.ContentLockedAtUtc);
            if (item.Question.Stimulus != null)
                Assert.Equal(item.OrderIndex == 1 ? after.UpdatedAtUtc : OldLock, item.Question.Stimulus.ContentLockedAtUtc);
        }

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentDraftService>();
        var editor = (await service.GetConfigurationEditorAsync(data.Teacher.Id, after.Id)).Value!;
        Assert.Equal(AssessmentStatus.Published, editor.Status);
        Assert.Equal(after.UpdatedAtUtc, editor.Input.ExpectedUpdatedAtUtc);
        Assert.Equal(37, editor.Input.DurationMinutes);
        var row = Assert.Single((await service.GetListAsync(data.Teacher.Id)).Value!);
        Assert.True(row.CanEditConfiguration);
        Assert.False(row.CanEditDraft);
        if (final)
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(after.Id, await db.Courses.Where(c => c.Id == after.CourseId)
                .Select(c => c.FinalAssessmentId).SingleAsync());
        }

        // API Draft cũ vẫn chỉ sửa Draft. Trang soạn câu hỏi dùng API riêng.
        string saved = Snapshot(after);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.GetDraftAsync(data.Teacher.Id, after.Id)).Access);
        Assert.False((await service.SaveDraftAsync(data.Teacher.Id, after.Id, editor.Input)).Success);
        var questions = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentQuestionService>();
        var questionEditor = await questions.GetEditorAsync(data.Teacher.Id, after.Id);
        Assert.Equal(LearningAccessResult.Allowed, questionEditor.Access);
        Assert.Equal(AssessmentStatus.Published, questionEditor.Value!.Status);
        AssertValidation(await questions.SaveAsync(data.Teacher.Id, after.Id,
            new UpdateAssessmentQuestionsRequest
            {
                ExpectedUpdatedAtUtc = after.UpdatedAtUtc,
                Items = new List<AssessmentQuestionInput>()
            }), "ít nhất 5 câu");
        Assert.Equal(saved, Snapshot(await ReadAsync(data)));
    }

    [Theory]
    [InlineData("WrongTarget", "TargetSkill")]
    [InlineData("BrokenMcq", "đúng một đáp án")]
    [InlineData("InvalidAudio", "URL audio")]
    [InlineData("FinalWithoutScore", "Final phải có PassingScore")]
    public async Task Save_InvalidDefinition_LeavesConfigurationVersionContentAndLocksUntouched(string fault, string message)
    {
        var data = await SeedAsync(final: fault == "FinalWithoutScore");
        var input = await InputAsync(data);
        input.Title = "Must not be saved";
        input.DurationMinutes = 45;
        if (fault == "WrongTarget") input.TargetSkill = EnglishSkill.Listening;
        if (fault == "FinalWithoutScore") input.PassingScore = null;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (fault == "BrokenMcq")
            {
                var id = data.Assessment.Questions.Single(q => q.OrderIndex == 4).QuestionId;
                await db.QuestionOptions.Where(o => o.QuestionId == id)
                    .ExecuteUpdateAsync(set => set.SetProperty(o => o.IsCorrect, false));
            }
            if (fault == "InvalidAudio")
            {
                var id = data.Assessment.Questions.Single(q => q.OrderIndex == 1).Question.StimulusId;
                await db.Stimuli.Where(s => s.Id == id)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.ResourceUrl, "/invalid-relative.mp3"));
            }
        }
        string before = Snapshot(await ReadAsync(data));

        AssertValidation(await SaveAsync(data, input), message);

        Assert.Equal(before, Snapshot(await ReadAsync(data)));
    }

    [Fact]
    public async Task Save_StaleVersion_CannotOverwriteTheLatestConfiguration()
    {
        var data = await SeedAsync();
        var first = await InputAsync(data);
        var stale = await InputAsync(data);
        first.DurationMinutes = 31;
        Assert.True((await SaveAsync(data, first)).Success);
        string before = Snapshot(await ReadAsync(data));
        stale.DurationMinutes = 99;

        AssertValidation(await SaveAsync(data, stale), "tải lại trang");

        Assert.Equal(before, Snapshot(await ReadAsync(data)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnyAttempt_EvenInProgress_BlocksEditorAndConfigurationWrites(bool graded)
    {
        var data = await SeedAsync();
        var input = await InputAsync(data);
        var student = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);
        var start = await StartAsync(data, student.Id);
        Assert.Equal(AttemptStartStatus.Started, start.Status);
        if (graded)
        {
            using var gradeScope = _factory.Services.CreateScope();
            var result = await gradeScope.ServiceProvider.GetRequiredService<IAttemptService>()
                .FinalizeAsync(student.Id, start.AttemptId!.Value);
            Assert.Equal(AttemptFinalizeStatus.Graded, result.Status);
        }
        string before = Snapshot(await ReadAsync(data));
        input.DurationMinutes = 99;

        AssertValidation(await SaveAsync(data, input), "chưa có Attempt");

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentDraftService>();
        Assert.Equal(LearningAccessResult.Forbidden,
            (await service.GetConfigurationEditorAsync(data.Teacher.Id, data.Assessment.Id)).Access);
        Assert.False(Assert.Single((await service.GetListAsync(data.Teacher.Id)).Value!).CanEditConfiguration);
        var questions = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentQuestionService>();
        Assert.Equal(LearningAccessResult.Forbidden,
            (await questions.GetEditorAsync(data.Teacher.Id, data.Assessment.Id)).Access);
        Assert.Equal(LearningAccessResult.Forbidden,
            (await questions.SaveAsync(data.Teacher.Id, data.Assessment.Id, await QuestionInputAsync(data))).Access);
        Assert.Equal(before, Snapshot(await ReadAsync(data)));
    }

    [Fact]
    public async Task OtherTeacherAndStudent_CannotReadOrSaveTheConfiguration()
    {
        var data = await SeedAsync();
        var input = await InputAsync(data);
        input.Title = "Foreign write";
        string before = Snapshot(await ReadAsync(data));
        foreach (string role in new[] { AppRoles.Teacher, AppRoles.Student })
        {
            var actor = await IdentityTestHelpers.CreateUserAsync(_factory, role);
            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentDraftService>();
            var expected = role == AppRoles.Teacher ? LearningAccessResult.NotFound : LearningAccessResult.Forbidden;
            Assert.Equal(expected, (await service.GetConfigurationEditorAsync(actor.Id, data.Assessment.Id)).Access);
            Assert.Equal(expected, (await service.SaveConfigurationAsync(actor.Id, data.Assessment.Id, input)).Access);
            var questions = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentQuestionService>();
            Assert.Equal(expected, (await questions.GetEditorAsync(actor.Id, data.Assessment.Id)).Access);
            Assert.Equal(expected, (await questions.SaveAsync(actor.Id, data.Assessment.Id,
                await QuestionInputAsync(data))).Access);
        }
        Assert.Equal(before, Snapshot(await ReadAsync(data)));
    }

    [Fact]
    public async Task SaveAndStudentStart_UseTheSameDefinitionForTheCreatedAttempt()
    {
        var data = await SeedAsync();
        var input = await InputAsync(data);
        input.DurationMinutes = 47;
        var student = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);

        // Hai scope/connection riêng giống hai request thật; cùng khóa Assessment.
        var saving = SaveAsync(data, input);
        var starting = StartAsync(data, student.Id);
        await Task.WhenAll(saving, starting);
        var saveResult = await saving;
        var startResult = await starting;

        Assert.Equal(AttemptStartStatus.Started, startResult.Status);
        var after = await ReadAsync(data);
        var attempt = Assert.Single(after.Attempts);
        Assert.Equal(saveResult.Success ? 47 : 20, after.DurationMinutes);
        Assert.Equal(attempt.StartedAtUtc.AddMinutes(after.DurationMinutes), attempt.DeadlineUtc);
        if (!saveResult.Success) AssertValidation(saveResult, "chưa có Attempt");
    }

    [Fact]
    public async Task FailureAfterSqlWrites_RollsBackMetadataVersionAndNewLocks()
    {
        var data = await SeedAsync();
        var input = await InputAsync(data);
        input.Title = "ROLLBACK_CONFIGURATION";
        string before = Snapshot(await ReadAsync(data));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var interceptor = new FailAfterConfigurationWrites(data.Assessment.Id);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(db.Database.GetConnectionString()).AddInterceptors(interceptor).Options;
        await using var serviceDb = new AppDbContext(options);
        var service = new TeacherAssessmentDraftService(serviceDb, NullLogger<TeacherAssessmentDraftService>.Instance,
            scope.ServiceProvider.GetRequiredService<IAssessmentDefinitionValidator>(),
            scope.ServiceProvider.GetRequiredService<IStimulusAudioStore>());

        AssertValidation(await service.SaveConfigurationAsync(data.Teacher.Id, data.Assessment.Id, input), "Chưa thể lưu");

        Assert.True(interceptor.SawWrittenConfigurationAndLocks);
        Assert.Null(serviceDb.Database.CurrentTransaction);
        Assert.Equal(before, Snapshot(await ReadAsync(data)));
    }

    [Fact]
    public async Task Http_PublishedForm_UsesServerStatusAndValidation_AndRejectsWritesAfterStart()
    {
        var data = await SeedAsync();
        using var client = IdentityTestHelpers.CreateClient(_factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, data.Teacher);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        string path = "/TeacherAssessments/Edit/" + data.Assessment.Id;
        using var page = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        string html = await page.Content.ReadAsStringAsync();
        AssertPublishedForm(html);
        var version = Regex.Match(html, "name=\"Input.ExpectedUpdatedAtUtc\"[^>]*value=\"([^\"]+)\"");
        Assert.True(version.Success);
        var fields = new Dictionary<string, string>
        {
            ["Input.Title"] = "HTTP configuration", ["Input.AssessmentType"] = "SkillAssessment",
            ["Input.TargetSkill"] = "Listening", ["Input.DurationMinutes"] = "35", ["Input.MaxAttempts"] = "4",
            ["Input.PassingScore"] = "75.25", ["Input.ExpectedUpdatedAtUtc"] = WebUtility.HtmlDecode(version.Groups[1].Value),
            ["Status"] = "Draft", ["Input.Status"] = "Draft", ["Input.CourseId"] = Guid.NewGuid().ToString(),
            ["Input.OwnerTeacherUserId"] = Guid.NewGuid().ToString()
        };
        string before = Snapshot(await ReadAsync(data));
        using var csrf = await client.PostAsync(path, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        Assert.Equal(before, Snapshot(await ReadAsync(data)));
        fields["__RequestVerificationToken"] = await IdentityTestHelpers.GetTokenAsync(client, path);
        using var invalid = await client.PostAsync(path, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        html = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("không khớp TargetSkill", WebUtility.HtmlDecode(html));
        AssertPublishedForm(html);
        Assert.Equal(before, Snapshot(await ReadAsync(data)));

        fields["Input.TargetSkill"] = "Reading";
        using var valid = await client.PostAsync(path, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        using var reload = await client.GetAsync(valid.Headers.Location);
        AssertPublishedForm(await reload.Content.ReadAsStringAsync());
        var after = await ReadAsync(data);
        Assert.Equal("HTTP configuration", after.Title);
        Assert.Equal(35, after.DurationMinutes);
        Assert.Equal(AssessmentStatus.Published, after.Status);
        Assert.Equal(data.Teacher.Id, after.OwnerTeacherUserId);
        Assert.Null(after.CourseId);

        var student = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);
        Assert.Equal(AttemptStartStatus.Started, (await StartAsync(data, student.Id)).Status);
        before = Snapshot(await ReadAsync(data));
        using var forbidden = await client.PostAsync(path, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, forbidden.StatusCode);
        Assert.Contains("/Account/AccessDenied", forbidden.Headers.Location!.ToString());
        Assert.Equal(before, Snapshot(await ReadAsync(data)));
    }

    [Fact]
    public async Task Http_PublishedQuestions_ReplacesReordersAndReweights_KeepsContentAndHistoricalLocks()
    {
        var data = await SeedAsync(withRemovedStimulus: true);
        var spare = await AddSpareQuestionAsync(data);
        var input = await QuestionInputAsync(data);
        Guid removedId = input.Items![4].QuestionId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Câu Archived đã thuộc đề được giữ; câu bị bỏ có Stimulus chưa có marker cũ.
            await db.Questions.Where(q => q.Id == input.Items[0].QuestionId)
                .ExecuteUpdateAsync(set => set.SetProperty(q => q.Status, QuestionStatus.Archived));
            // Stimulus đã gắn từ lúc seed ban đầu, không sửa Question đã khóa để dựng fixture.
        }
        string bankBefore = await BankSnapshotAsync(data, includeLocks: false);
        input.Items[4].QuestionId = spare.Id;
        input.Items.Reverse();
        input.Items[0].Points = 2.75m;
        using var client = IdentityTestHelpers.CreateClient(_factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, data.Teacher);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var list = await client.GetAsync("/TeacherAssessments");
        Assert.Contains("/TeacherAssessmentQuestions/Index/" + data.Assessment.Id, await list.Content.ReadAsStringAsync());
        string path = "/TeacherAssessmentQuestions/Index/" + data.Assessment.Id;
        using var page = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl!.NoStore);
        Assert.Contains("Đề đang mở và chưa có lượt làm", WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync()));
        string token = await IdentityTestHelpers.GetTokenAsync(client, path);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/TeacherAssessmentQuestions/Save/" + data.Assessment.Id);
        request.Content = JsonContent.Create(input);
        request.Headers.Add("RequestVerificationToken", token);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await ReadAsync(data);
        Assert.Equal(AssessmentStatus.Published, after.Status);
        Assert.NotEqual(Version, after.UpdatedAtUtc);
        var saved = after.Questions.OrderBy(q => q.OrderIndex).ToList();
        Assert.Equal(input.Items.Select(q => q.QuestionId), saved.Select(q => q.QuestionId));
        Assert.Equal(input.Items.Select(q => q.Points!.Value), saved.Select(q => q.Points));
        Assert.Equal(Enumerable.Range(0, 5), saved.Select(q => q.OrderIndex));
        Assert.Empty(after.Attempts);
        Assert.Equal(bankBefore, await BankSnapshotAsync(data, includeLocks: false));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bank = await db.Questions.AsNoTracking().Include(q => q.Stimulus)
                .Where(q => q.OwnerTeacherUserId == data.Teacher.Id).ToListAsync();
            Assert.Equal(6, bank.Count); // Không INSERT trùng Question/Stimulus từ graph validation.
            Assert.All(bank, q => Assert.NotNull(q.ContentLockedAtUtc));
            Assert.All(bank.Where(q => q.Stimulus != null), q => Assert.NotNull(q.Stimulus!.ContentLockedAtUtc));
            Assert.Equal(OldLock, bank.Single(q => q.Id == data.Assessment.Questions.Single(i => i.OrderIndex == 0).QuestionId).ContentLockedAtUtc);
            Assert.Equal(OldLock, bank.Single(q => q.Id == data.Assessment.Questions.Single(i => i.OrderIndex == 2).QuestionId).Stimulus!.ContentLockedAtUtc);
            Assert.Equal(after.UpdatedAtUtc, bank.Single(q => q.Id == removedId).Stimulus!.ContentLockedAtUtc);
            var editor = (await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentQuestionService>()
                .GetEditorAsync(data.Teacher.Id, data.Assessment.Id)).Value!;
            Assert.Equal(AssessmentStatus.Published, editor.Status);
            Assert.Equal(saved.Select(q => q.QuestionId), editor.Items.Select(q => q.QuestionId));
            Assert.Equal(saved.Select(q => q.Points), editor.Items.Select(q => q.Points));
        }
        string snapshot = Snapshot(after);
        string locks = await BankSnapshotAsync(data);
        AssertValidation(await SaveQuestionsAsync(data, input), "tải lại trang");
        Assert.Equal(snapshot, Snapshot(await ReadAsync(data)));
        Assert.Equal(locks, await BankSnapshotAsync(data));
        using var reload = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, reload.StatusCode);
        Assert.Contains("value=\"2.75\"", await reload.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("TooFew", "ít nhất 5 câu")]
    [InlineData("WrongSkill", "TargetSkill")]
    [InlineData("ArchivedNew", "Archived")]
    [InlineData("MissingAudio", "Stimulus loại Audio")]
    [InlineData("ForeignQuestion", "Không tìm thấy")]
    [InlineData("InvalidFinal", "Final phải có PassingScore")]
    [InlineData("InvalidMetadata", "Cấu hình")]
    public async Task PublishedQuestions_InvalidCandidate_LeavesDefinitionAndAllLocksUntouched(string fault, string message)
    {
        var data = await SeedAsync(final: fault == "InvalidFinal");
        var spare = await AddSpareQuestionAsync(data);
        var input = await QuestionInputAsync(data);
        input.Items![4].QuestionId = spare.Id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (fault == "TooFew") input.Items.RemoveAt(0);
            if (fault == "WrongSkill")
                await db.Questions.Where(q => q.Id == spare.Id).ExecuteUpdateAsync(set => set.SetProperty(q => q.PrimarySkill, EnglishSkill.Grammar));
            if (fault == "ArchivedNew")
                await db.Questions.Where(q => q.Id == spare.Id).ExecuteUpdateAsync(set => set.SetProperty(q => q.Status, QuestionStatus.Archived));
            if (fault == "MissingAudio")
            {
                await db.Assessments.Where(a => a.Id == data.Assessment.Id).ExecuteUpdateAsync(set => set
                    .SetProperty(a => a.AssessmentType, AssessmentType.PracticeExam).SetProperty(a => a.TargetSkill, (EnglishSkill?)null));
                await db.Questions.Where(q => q.Id == spare.Id).ExecuteUpdateAsync(set => set.SetProperty(q => q.PrimarySkill, EnglishSkill.Listening));
            }
            if (fault == "ForeignQuestion") input.Items[4].QuestionId = (await SeedAsync()).Assessment.Questions.First().QuestionId;
            if (fault == "InvalidFinal")
                await db.Assessments.Where(a => a.Id == data.Assessment.Id).ExecuteUpdateAsync(set => set.SetProperty(a => a.PassingScore, (decimal?)null));
            if (fault == "InvalidMetadata")
                await db.Assessments.Where(a => a.Id == data.Assessment.Id).ExecuteUpdateAsync(set => set.SetProperty(a => a.Title, "   "));
        }
        string before = Snapshot(await ReadAsync(data));
        string bankBefore = await BankSnapshotAsync(data);

        AssertValidation(await SaveQuestionsAsync(data, input), message);

        Assert.Equal(before, Snapshot(await ReadAsync(data)));
        Assert.Equal(bankBefore, await BankSnapshotAsync(data));
    }

    [Fact]
    public async Task PublishedQuestions_FailureAfterDeletingLinks_RollsBackOldDefinitionAndNewLocks()
    {
        var data = await SeedAsync();
        var spare = await AddSpareQuestionAsync(data);
        var input = await QuestionInputAsync(data);
        input.Items![4].QuestionId = spare.Id;
        string before = Snapshot(await ReadAsync(data));
        string bankBefore = await BankSnapshotAsync(data);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var interceptor = new FailAfterQuestionDeletion(data.Assessment.Id, spare.Id);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(db.Database.GetConnectionString()).AddInterceptors(interceptor).Options;
        await using var serviceDb = new AppDbContext(options);
        var validator = scope.ServiceProvider.GetRequiredService<IAssessmentDefinitionValidator>();
        var drafts = new TeacherAssessmentDraftService(serviceDb, NullLogger<TeacherAssessmentDraftService>.Instance, validator,
            scope.ServiceProvider.GetRequiredService<IStimulusAudioStore>());
        var service = new TeacherAssessmentQuestionService(serviceDb, drafts,
            scope.ServiceProvider.GetRequiredService<IQuestionUsageValidator>(),
            NullLogger<TeacherAssessmentQuestionService>.Instance, validator,
            scope.ServiceProvider.GetRequiredService<IStimulusAudioStore>());

        AssertValidation(await service.SaveAsync(data.Teacher.Id, data.Assessment.Id, input), "Chưa thể lưu");

        Assert.True(interceptor.SawDeletedLinksAndNewLock);
        Assert.Null(serviceDb.Database.CurrentTransaction);
        Assert.Equal(before, Snapshot(await ReadAsync(data)));
        Assert.Equal(bankBefore, await BankSnapshotAsync(data));
    }

    [Fact]
    public async Task PublishedQuestions_SaveAndStudentStart_CannotChangeDefinitionAfterFirstAttempt()
    {
        var data = await SeedAsync();
        var spare = await AddSpareQuestionAsync(data);
        var input = await QuestionInputAsync(data);
        input.Items![4].QuestionId = spare.Id;
        input.Items[0].Points = 9.25m;
        var student = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);

        var saving = SaveQuestionsAsync(data, input);
        var starting = StartAsync(data, student.Id);
        await Task.WhenAll(saving, starting);
        var save = await saving;
        var start = await starting;

        Assert.Equal(AttemptStartStatus.Started, start.Status);
        var after = await ReadAsync(data);
        Assert.Single(after.Attempts);
        Assert.Equal(save.Success, after.Questions.Any(q => q.QuestionId == spare.Id));
        Assert.Equal(save.Success ? 9.25m : 1m, after.Questions.Single(q => q.OrderIndex == 0).Points);
        if (!save.Success) Assert.Equal(LearningAccessResult.Forbidden, save.Access);
        string before = Snapshot(after);
        input.ExpectedUpdatedAtUtc = after.UpdatedAtUtc;
        Assert.Equal(LearningAccessResult.Forbidden, (await SaveQuestionsAsync(data, input)).Access);
        Assert.Equal(before, Snapshot(await ReadAsync(data)));
    }

    private static void AssertPublishedForm(string html)
    {
        Assert.Contains("Đề đang mở và chưa có lượt làm", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("/TeacherAssessments/Publish/", html);
        Assert.Contains("/TeacherAssessmentQuestions/Index/", html);
    }

    private async Task<UpdateAssessmentQuestionsRequest> QuestionInputAsync(SeedData data)
    {
        var assessment = await ReadAsync(data);
        return new UpdateAssessmentQuestionsRequest
        {
            ExpectedUpdatedAtUtc = assessment.UpdatedAtUtc,
            Items = assessment.Questions.OrderBy(q => q.OrderIndex).Select(q => new AssessmentQuestionInput
            {
                QuestionId = q.QuestionId, Points = q.Points
            }).ToList()
        };
    }

    private async Task<WriteOutcome> SaveQuestionsAsync(SeedData data, UpdateAssessmentQuestionsRequest input)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentQuestionService>()
            .SaveAsync(data.Teacher.Id, data.Assessment.Id, input);
    }

    private async Task<Question> AddSpareQuestionAsync(SeedData data)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var question = new Question
        {
            OwnerTeacherUserId = data.Teacher.Id, Content = "New question before first Attempt", PrimarySkill = EnglishSkill.Reading,
            Stimulus = new Stimulus
            {
                OwnerTeacherUserId = data.Teacher.Id, StimulusType = StimulusType.Passage, ContentText = "New passage."
            },
            Options = new List<QuestionOption>
            {
                new QuestionOption { Content = "Correct", IsCorrect = true, OrderIndex = 0 },
                new QuestionOption { Content = "Wrong", OrderIndex = 1 }
            }
        };
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        return question;
    }

    // Bao gồm cả câu không còn thuộc đề để phát hiện mất/mở khóa nội dung cũ.
    private async Task<string> BankSnapshotAsync(SeedData data, bool includeLocks = true)
    {
        using var scope = _factory.Services.CreateScope();
        var questions = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Questions.AsNoTracking()
            .Include(q => q.Options).Include(q => q.Stimulus)
            .Where(q => q.OwnerTeacherUserId == data.Teacher.Id).OrderBy(q => q.Id).ToListAsync();
        return JsonSerializer.Serialize(questions.Select(q => new
        {
            q.Id, q.OwnerTeacherUserId, q.Content, q.PrimarySkill, q.Status, q.StimulusId, q.Level, q.Explanation, q.UpdatedAtUtc,
            Lock = includeLocks ? q.ContentLockedAtUtc : null,
            Options = q.Options.OrderBy(o => o.OrderIndex).Select(o => new { o.Id, o.Content, o.IsCorrect, o.OrderIndex, o.UpdatedAtUtc }),
            Stimulus = q.Stimulus == null ? null : new
            {
                q.Stimulus.Id, q.Stimulus.OwnerTeacherUserId, q.Stimulus.StimulusType, q.Stimulus.ContentText, q.Stimulus.ResourceUrl, q.Stimulus.UpdatedAtUtc,
                Lock = includeLocks ? q.Stimulus.ContentLockedAtUtc : null
            }
        }));
    }

    private async Task<AssessmentDraftRequest> InputAsync(SeedData data)
    {
        using var scope = _factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentDraftService>()
            .GetConfigurationEditorAsync(data.Teacher.Id, data.Assessment.Id);
        Assert.Equal(LearningAccessResult.Allowed, result.Access);
        return Assert.IsType<TeacherAssessmentConfigurationEditor>(result.Value).Input;
    }

    private async Task<WriteOutcome> SaveAsync(SeedData data, AssessmentDraftRequest input)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentDraftService>()
            .SaveConfigurationAsync(data.Teacher.Id, data.Assessment.Id, input);
    }

    private async Task<AttemptStartResult> StartAsync(SeedData data, Guid studentId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAttemptService>().StartAsync(studentId, data.Assessment.Id);
    }

    private async Task<AssessmentEntity> ReadAsync(SeedData data)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Assessments.AsNoTracking().AsSplitQuery()
            .Include(a => a.Questions).ThenInclude(q => q.Question).ThenInclude(q => q.Options)
            .Include(a => a.Questions).ThenInclude(q => q.Question).ThenInclude(q => q.Stimulus)
            .Include(a => a.Attempts).ThenInclude(a => a.Answers)
            .Include(a => a.Attempts).ThenInclude(a => a.SkillResults)
            .SingleAsync(a => a.Id == data.Assessment.Id);
    }

    // Chiếu dữ liệu rõ ràng để không serialize vòng navigation của EF.
    private static string Content(AssessmentEntity a) => JsonSerializer.Serialize(a.Questions.OrderBy(q => q.OrderIndex).Select(q => new
    {
        q.Id, q.QuestionId, q.OrderIndex, q.Points, q.Question.OwnerTeacherUserId, q.Question.Content,
        q.Question.PrimarySkill, q.Question.Status, q.Question.Explanation, q.Question.Level,
        q.Question.StimulusId, q.Question.UpdatedAtUtc,
        Options = q.Question.Options.OrderBy(o => o.OrderIndex).Select(o => new { o.Id, o.OrderIndex, o.Content, o.IsCorrect, o.UpdatedAtUtc }),
        Stimulus = q.Question.Stimulus == null ? null : new
        {
            q.Question.Stimulus.Id, q.Question.Stimulus.OwnerTeacherUserId, q.Question.Stimulus.StimulusType,
            q.Question.Stimulus.ContentText, q.Question.Stimulus.ResourceUrl, q.Question.Stimulus.UpdatedAtUtc
        }
    }));

    private static string Snapshot(AssessmentEntity a) => JsonSerializer.Serialize(new
    {
        a.Id, a.Title, a.Description, a.OwnerTeacherUserId, a.Status, a.UpdatedAtUtc, a.CourseId,
        a.AssessmentType, a.TargetSkill, a.DurationMinutes, a.MaxAttempts, a.PassingScore, Content = Content(a),
        Locks = a.Questions.OrderBy(q => q.OrderIndex).Select(q => new
        { q.Question.ContentLockedAtUtc, StimulusLock = q.Question.Stimulus?.ContentLockedAtUtc }),
        Attempts = a.Attempts.OrderBy(t => t.Id).Select(t => new
        {
            t.Id, t.Status, t.StudentUserId, t.StartedAtUtc, t.DeadlineUtc, t.OverallScore, t.Passed, t.FinalizedAtUtc, t.FinalizationReason,
            Answers = t.Answers.OrderBy(answer => answer.Id).Select(answer => new { answer.Id, answer.SelectedOptionId, answer.SavedAtUtc }),
            Results = t.SkillResults.OrderBy(r => r.Id).Select(r => new { r.Id, r.Skill, r.Score, r.QuestionCount })
        })
    });

    private static void AssertValidation(WriteOutcome result, string message)
    {
        Assert.Equal(LearningAccessResult.Allowed, result.Access);
        Assert.False(result.Success);
        Assert.Contains(result.Errors.SelectMany(e => e.Value), error => error.Contains(message, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<SeedData> SeedAsync(bool final = false, bool withRemovedStimulus = false)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passage = new Stimulus
        {
            OwnerTeacherUserId = teacher.Id, StimulusType = StimulusType.Passage, ContentText = "Shared passage."
        };
        passage.LockContent(OldLock);
        var audio = new Stimulus
        {
            OwnerTeacherUserId = teacher.Id, StimulusType = StimulusType.Audio, ResourceUrl = "https://media.example.test/config.mp3"
        };
        // Dựng Published cũ thiếu một số dấu khóa để kiểm tra giữ/bổ sung dấu khóa an toàn.
        var assessment = new AssessmentEntity
        {
            OwnerTeacherUserId = teacher.Id, Title = "Published configuration", Status = AssessmentStatus.Published,
            AssessmentType = AssessmentType.SkillAssessment, TargetSkill = EnglishSkill.Reading,
            DurationMinutes = 20, MaxAttempts = 3, PassingScore = 70m, UpdatedAtUtc = Version,
            Course = final ? new Course { OwnerTeacherUserId = teacher.Id, Title = "Final course", Status = CourseStatus.Published } : null
        };
        for (int index = 0; index < 5; index++)
        {
            var question = new Question
            {
                OwnerTeacherUserId = teacher.Id, Content = "Configuration question " + index,
                PrimarySkill = EnglishSkill.Reading, Status = QuestionStatus.Published,
                Stimulus = index == 0 || index == 2 ? passage : index == 1 ? audio
                    : index == 4 && withRemovedStimulus ? new Stimulus
                    { OwnerTeacherUserId = teacher.Id, StimulusType = StimulusType.Passage, ContentText = "Removed passage." } : null,
                Options = new List<QuestionOption>
                {
                    new QuestionOption { Content = "Correct", IsCorrect = true, OrderIndex = 0 },
                    new QuestionOption { Content = "Wrong", OrderIndex = 1 }
                }
            };
            if (index == 0) question.LockContent(OldLock);
            assessment.Questions.Add(new AssessmentQuestion { Question = question, OrderIndex = index, Points = index + 1m });
        }
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();
        if (final)
        {
            assessment.Course!.FinalAssessmentId = assessment.Id;
            await db.SaveChangesAsync();
        }
        return new SeedData(teacher, assessment);
    }

    private sealed record SeedData(ApplicationUser Teacher, AssessmentEntity Assessment);

    // Kiểm tra SQL đã xóa liên kết và ghi marker trước khi ép lỗi rollback.
    private sealed class FailAfterQuestionDeletion(Guid assessmentId, Guid spareId) : SaveChangesInterceptor
    {
        public bool SawDeletedLinksAndNewLock { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            var db = (AppDbContext)eventData.Context!;
            bool noLinks = !await db.AssessmentQuestions.AsNoTracking().AnyAsync(q => q.AssessmentId == assessmentId, cancellationToken);
            bool locked = await db.Questions.AsNoTracking().AnyAsync(q => q.Id == spareId
                && q.ContentLockedAtUtc != null && q.Stimulus!.ContentLockedAtUtc != null, cancellationToken);
            SawDeletedLinksAndNewLock = noLinks && locked;
            throw new InvalidOperationException("TEST_ROLLBACK_PUBLISHED_QUESTION_LINKS_AND_LOCKS");
        }
    }

    // Ép lỗi sau SQL nhưng trước Commit để kiểm tra rollback thật, không chỉ lỗi validation.
    private sealed class FailAfterConfigurationWrites(Guid assessmentId) : SaveChangesInterceptor
    {
        public bool SawWrittenConfigurationAndLocks { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            var db = (AppDbContext)eventData.Context!;
            bool changed = await db.Assessments.AsNoTracking().AnyAsync(a => a.Id == assessmentId
                && a.Title == "ROLLBACK_CONFIGURATION" && a.UpdatedAtUtc != Version, cancellationToken);
            bool locked = await db.AssessmentQuestions.AsNoTracking().AnyAsync(q => q.AssessmentId == assessmentId
                && q.OrderIndex == 4 && q.Question.ContentLockedAtUtc != null, cancellationToken);
            SawWrittenConfigurationAndLocks = changed && locked;
            throw new InvalidOperationException("TEST_ROLLBACK_CONFIGURATION_BEFORE_COMMIT");
        }
    }
}
