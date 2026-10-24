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
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

using AssessmentEntity = EnglishLearningPlatform.Domain.Entities.Assessment;

namespace EnglishLearningPlatform.IntegrationTests;

// SQL Server test riêng của factory; không đọc/ghi database demo trên máy người dùng.
public sealed class TeacherAssessmentQuestionsTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly DateTimeOffset InitialVersion =
        new DateTimeOffset(2026, 10, 18, 2, 0, 0, TimeSpan.Zero);
    private readonly IntegrationTestFactory _factory;

    public TeacherAssessmentQuestionsTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Save_ReordersPersistsPointLimitsRemovesAndClears_WithoutChangingQuestionBank()
    {
        var data = await SeedAsync();
        var input = Selection(data);
        input.Items![0].Points = 999999.99m;
        input.Items[1].Points = 0.01m;

        Assert.True((await SaveAsync(data, input)).Success);
        var saved = await ReadAsync(data);
        Assert.Equal(new[] { data.SecondId, data.FirstId }, Ordered(saved).Select(q => q.QuestionId));
        Assert.Equal(new[] { 999999.99m, 0.01m }, Ordered(saved).Select(q => q.Points));
        Assert.Equal(new[] { 0, 1 }, Ordered(saved).Select(q => q.OrderIndex));
        Assert.NotEqual(InitialVersion, saved.UpdatedAtUtc);

        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentQuestionService>();
            var editor = await service.GetEditorAsync(data.Teacher.Id, data.AssessmentId);
            Assert.Equal(LearningAccessResult.Allowed, editor.Access);
            Assert.NotNull(editor.Value);
            Assert.Equal(saved.UpdatedAtUtc, editor.Value.UpdatedAtUtc);
            Assert.Equal(Ordered(saved).Select(q => q.QuestionId), editor.Value.Items.Select(q => q.QuestionId));
            Assert.Equal(Ordered(saved).Select(q => q.Points), editor.Value.Items.Select(q => q.Points));
        }

        input = Selection(data, saved.UpdatedAtUtc);
        input.Items!.RemoveAt(0);
        Assert.True((await SaveAsync(data, input)).Success);
        var reduced = await ReadAsync(data);
        Assert.Equal(data.FirstId, Assert.Single(reduced.Questions).QuestionId);
        input.Items.Clear();
        input.ExpectedUpdatedAtUtc = reduced.UpdatedAtUtc;
        Assert.True((await SaveAsync(data, input)).Success);
        Assert.Empty((await ReadAsync(data)).Questions);

        using var checkScope = _factory.Services.CreateScope();
        var db = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var questions = await db.Questions.Include(q => q.Options)
            .Where(q => q.OwnerTeacherUserId == data.Teacher.Id).ToListAsync();
        Assert.Equal(2, questions.Count);
        Assert.All(questions, q =>
        {
            Assert.Null(q.UpdatedAtUtc);
            Assert.Null(q.ContentLockedAtUtc);
            Assert.Equal(QuestionStatus.Draft, q.Status);
            Assert.Equal(2, q.Options.Count);
            Assert.Single(q.Options, option => option.IsCorrect);
        });
        Assert.False(await db.Attempts.AnyAsync(a => a.AssessmentId == data.AssessmentId));
    }

    [Theory]
    [InlineData("MissingItems", "thiếu danh sách")]
    [InlineData("NullRow", "ID câu hỏi")]
    [InlineData("EmptyId", "ID câu hỏi")]
    [InlineData("Duplicate", "trùng")]
    [InlineData("MissingQuestion", "Không tìm thấy")]
    [InlineData("ForeignQuestion", "Không tìm thấy")]
    [InlineData("ArchivedNew", "Archived")]
    [InlineData("MissingPoints", "points")]
    [InlineData("ZeroPoints", "points")]
    [InlineData("NegativePoints", "points")]
    [InlineData("OverLimit", "points")]
    [InlineData("Precision", "points")]
    [InlineData("InvalidMcq", "đúng một đáp án")]
    [InlineData("TooFewOptions", "ít nhất hai lựa chọn")]
    [InlineData("WrongSkill", "TargetSkill")]
    [InlineData("BlankPassage", "passage")]
    [InlineData("MissingAudio", "audio stimulus")]
    [InlineData("ForeignStimulus", "Stimulus không thuộc")]
    public async Task Save_InvalidSelection_LeavesOldLinksPointsAndVersionUntouched(string fault, string message)
    {
        var data = await SeedAsync();
        var input = Selection(data);
        switch (fault)
        {
            case "MissingItems": input.Items = null; break;
            case "NullRow": input.Items![0] = null!; break;
            case "EmptyId": input.Items![0].QuestionId = Guid.Empty; break;
            case "Duplicate": input.Items![0].QuestionId = data.FirstId; break;
            case "MissingQuestion": input.Items![0].QuestionId = Guid.NewGuid(); break;
            case "ForeignQuestion": input.Items![0].QuestionId = (await SeedAsync()).SecondId; break;
            case "MissingPoints": input.Items![0].Points = null; break;
            case "ZeroPoints": input.Items![0].Points = 0m; break;
            case "NegativePoints": input.Items![0].Points = -1m; break;
            case "OverLimit": input.Items![0].Points = 1000000m; break;
            case "Precision": input.Items![0].Points = 1.001m; break;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (fault == "ArchivedNew")
                await db.Questions.Where(q => q.Id == data.SecondId)
                    .ExecuteUpdateAsync(set => set.SetProperty(q => q.Status, QuestionStatus.Archived));
            if (fault == "InvalidMcq")
                await db.QuestionOptions.Where(o => o.QuestionId == data.SecondId)
                    .ExecuteUpdateAsync(set => set.SetProperty(o => o.IsCorrect, false));
            if (fault == "TooFewOptions")
                await db.QuestionOptions.Where(o => o.QuestionId == data.SecondId && !o.IsCorrect).ExecuteDeleteAsync();
            if (fault == "WrongSkill")
            {
                await db.Assessments.Where(a => a.Id == data.AssessmentId).ExecuteUpdateAsync(set => set
                    .SetProperty(a => a.AssessmentType, AssessmentType.SkillAssessment)
                    .SetProperty(a => a.TargetSkill, EnglishSkill.Reading));
                await db.Questions.Where(q => q.Id == data.SecondId)
                    .ExecuteUpdateAsync(set => set.SetProperty(q => q.PrimarySkill, EnglishSkill.Grammar));
            }
            if (fault == "BlankPassage")
                await db.Stimuli.Where(s => s.Id == data.StimulusId)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.ContentText, "   "));
            if (fault == "MissingAudio")
                await db.Questions.Where(q => q.Id == data.SecondId)
                    .ExecuteUpdateAsync(set => set.SetProperty(q => q.PrimarySkill, EnglishSkill.Listening));
            if (fault == "ForeignStimulus")
            {
                var foreign = await SeedAsync();
                await db.Questions.Where(q => q.Id == data.SecondId)
                    .ExecuteUpdateAsync(set => set.SetProperty(q => q.StimulusId, foreign.StimulusId));
            }
        }
        var before = await ReadAsync(data);

        var result = await SaveAsync(data, input);

        AssertValidation(result, message);
        AssertUnchanged(before, await ReadAsync(data));
    }

    [Fact]
    public async Task Save_ExistingArchivedQuestion_CanBeKeptReorderedAndReweighted()
    {
        var data = await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Questions.Where(q => q.Id == data.FirstId)
                .ExecuteUpdateAsync(set => set.SetProperty(q => q.Status, QuestionStatus.Archived));
        }

        Assert.True((await SaveAsync(data, Selection(data))).Success);
        var saved = await ReadAsync(data);
        Assert.Equal(new[] { data.SecondId, data.FirstId }, Ordered(saved).Select(q => q.QuestionId));
        Assert.Equal(2.5m, Ordered(saved)[1].Points);
    }

    [Fact]
    public async Task ConfigurationAndQuestionPages_RejectEachOthersStaleVersions()
    {
        var data = await SeedAsync();
        Assert.True((await SaveAsync(data, Selection(data))).Success);
        var questionVersion = (await ReadAsync(data)).UpdatedAtUtc;
        using var scope = _factory.Services.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentDraftService>();
        var edit = (await drafts.GetDraftAsync(data.Teacher.Id, data.AssessmentId)).Value!;
        edit.ExpectedUpdatedAtUtc = InitialVersion;
        edit.Title = "Stale config";
        AssertValidation(await drafts.SaveDraftAsync(data.Teacher.Id, data.AssessmentId, edit), "tải lại trang");

        edit.ExpectedUpdatedAtUtc = questionVersion;
        edit.Title = "Fresh config";
        Assert.True((await drafts.SaveDraftAsync(data.Teacher.Id, data.AssessmentId, edit)).Success);
        var before = await ReadAsync(data);
        AssertValidation(await SaveAsync(data, Selection(data, questionVersion)), "tải lại trang");
        AssertUnchanged(before, await ReadAsync(data));
    }

    [Theory]
    [InlineData(AssessmentStatus.Published, true)]
    [InlineData(AssessmentStatus.Unpublished, false)]
    [InlineData(AssessmentStatus.Archived, false)]
    [InlineData(AssessmentStatus.Draft, true)]
    public async Task NonEditableStatusOrExistingAttempt_BlocksEditorAndSaving(AssessmentStatus status, bool hasAttempt)
    {
        var data = await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Assessments.Where(a => a.Id == data.AssessmentId)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, status));
            if (hasAttempt)
            {
                var student = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);
                db.Attempts.Add(new Attempt
                {
                    AssessmentId = data.AssessmentId, StudentUserId = student.Id,
                    DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(20)
                });
                await db.SaveChangesAsync();
            }
        }
        var before = await ReadAsync(data);
        using var checkScope = _factory.Services.CreateScope();
        var service = checkScope.ServiceProvider.GetRequiredService<ITeacherAssessmentQuestionService>();

        Assert.Equal(LearningAccessResult.Forbidden,
            (await service.GetEditorAsync(data.Teacher.Id, data.AssessmentId)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await SaveAsync(data, Selection(data))).Access);
        AssertUnchanged(before, await ReadAsync(data));
    }

    [Theory]
    [InlineData("OtherTeacher", LearningAccessResult.NotFound)]
    [InlineData("Student", LearningAccessResult.Forbidden)]
    [InlineData("LockedTeacher", LearningAccessResult.Forbidden)]
    public async Task ForeignOrUnauthorizedActor_CannotReadOrSave(string actorKind, LearningAccessResult expected)
    {
        var data = await SeedAsync();
        Guid actorId = data.Teacher.Id;
        if (actorKind == "OtherTeacher")
            actorId = (await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher)).Id;
        if (actorKind == "Student")
            actorId = (await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student)).Id;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (actorKind == "LockedTeacher")
            await db.Users.Where(u => u.Id == actorId)
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.AccountStatus, AccountStatus.Locked));
        var before = await ReadAsync(data);
        var service = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentQuestionService>();

        Assert.Equal(expected, (await service.GetEditorAsync(actorId, data.AssessmentId)).Access);
        Assert.Equal(expected, (await service.SaveAsync(actorId, data.AssessmentId, Selection(data))).Access);
        AssertUnchanged(before, await ReadAsync(data));
    }

    [Fact]
    public async Task FailureAfterDeletingOldLinks_RollsBackWholeTransaction()
    {
        var data = await SeedAsync();
        var before = await ReadAsync(data);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var interceptor = new RejectNewLinksInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(db.Database.GetConnectionString()).AddInterceptors(interceptor).Options;
        await using var serviceDb = new AppDbContext(options);
        var drafts = new TeacherAssessmentDraftService(serviceDb,
            NullLogger<TeacherAssessmentDraftService>.Instance,
            scope.ServiceProvider.GetRequiredService<IAssessmentDefinitionValidator>(),
            scope.ServiceProvider.GetRequiredService<IStimulusAudioStore>());
        var service = new TeacherAssessmentQuestionService(serviceDb, drafts,
            scope.ServiceProvider.GetRequiredService<IQuestionUsageValidator>(),
            NullLogger<TeacherAssessmentQuestionService>.Instance,
            scope.ServiceProvider.GetRequiredService<IAssessmentDefinitionValidator>(),
            scope.ServiceProvider.GetRequiredService<IStimulusAudioStore>());

        var result = await service.SaveAsync(data.Teacher.Id, data.AssessmentId, Selection(data));

        Assert.True(interceptor.SawOldDeletion);
        Assert.True(interceptor.RejectedInsertion);
        AssertValidation(result, "Chưa thể lưu");
        Assert.Null(serviceDb.Database.CurrentTransaction);
        // Đọc từ context khác để không nhầm entity cache với dữ liệu đã commit.
        AssertUnchanged(before, await ReadAsync(data));
    }

    [Fact]
    public async Task Http_SearchPaginationAndJsonSave_KeepAllSelectedQuestionsAndInvariantPoints()
    {
        var data = await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Assessments.Where(a => a.Id == data.AssessmentId).ExecuteUpdateAsync(set => set
                .SetProperty(a => a.AssessmentType, AssessmentType.SkillAssessment)
                .SetProperty(a => a.TargetSkill, EnglishSkill.Reading));
            db.Questions.Add(NewQuestion(data.Teacher.Id, "WRONG_SKILL_HIDDEN", EnglishSkill.Grammar));
            await db.SaveChangesAsync();
            await db.Questions.Where(q => q.OwnerTeacherUserId == data.Teacher.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(q => q.Level, "A1"));
        }
        using var client = await LoginAsync(data.Teacher);
        string path = Path(data) + "?Search=TASK2&Level=A1&Difficulty=Easy&PrimarySkill=Grammar&PageSize=1";
        using var page = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl!.NoStore);
        string html = await page.Content.ReadAsStringAsync();
        Assert.Contains("/js/teacher-assessment-questions.js", html);
        Assert.Contains(data.FirstId.ToString(), SelectedList(html));
        Assert.DoesNotContain("WRONG_SKILL_HIDDEN", html);
        var next = Regex.Match(html, "<a\\b[^>]*data-page[^>]*>");
        Assert.True(next.Success);
        var href = WebUtility.HtmlDecode(Regex.Match(next.Value, "href=\"([^\"]+)\"").Groups[1].Value);
        var query = QueryHelpers.ParseQuery(new Uri(new Uri("http://localhost"), href).Query);
        Assert.Equal("2", query["PageNumber"].ToString());
        Assert.Equal("Reading", query["PrimarySkill"].ToString());
        Assert.Equal("TASK2", query["Search"].ToString());
        Assert.Equal("A1", query["Level"].ToString());
        Assert.False(query.ContainsKey("Difficulty"));
        using var nextPage = await client.GetAsync(href);
        Assert.Equal(HttpStatusCode.OK, nextPage.StatusCode);
        Assert.Contains(data.FirstId.ToString(), SelectedList(await nextPage.Content.ReadAsStringAsync()));

        string token = await IdentityTestHelpers.GetTokenAsync(client, Path(data));
        using var save = await PostAsync(client, data, Selection(data), token);
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        using var json = JsonDocument.Parse(await save.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        var saved = await ReadAsync(data);
        Assert.Equal(new[] { 1.25m, 2.5m }, Ordered(saved).Select(q => q.Points));
        using var reload = await client.GetAsync(Path(data));
        string selected = SelectedList(await reload.Content.ReadAsStringAsync());
        Assert.True(selected.IndexOf(data.SecondId.ToString(), StringComparison.Ordinal)
            < selected.IndexOf(data.FirstId.ToString(), StringComparison.Ordinal));
        Assert.Contains("value=\"1.25\"", selected);

        var invalid = Selection(data, saved.UpdatedAtUtc);
        invalid.Items![0].Points = 0m;
        using var bad = await PostAsync(client, data, invalid, token);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        AssertUnchanged(saved, await ReadAsync(data));
    }

    [Fact]
    public async Task Http_GuestForeignOwnerAndMissingCsrf_CannotReadOrModifyDraft()
    {
        var data = await SeedAsync();
        var foreign = await SeedAsync();
        var before = await ReadAsync(data);
        using var guest = IdentityTestHelpers.CreateClient(_factory);
        using var anonymous = await guest.GetAsync(Path(data));
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/Account/Login", anonymous.Headers.Location!.ToString());

        using var otherClient = await LoginAsync(foreign.Teacher);
        using var foreignRead = await otherClient.GetAsync(Path(data));
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        string token = await IdentityTestHelpers.GetTokenAsync(otherClient, Path(foreign));
        using var foreignWrite = await PostAsync(otherClient, data, Selection(data), token);
        Assert.Equal(HttpStatusCode.NotFound, foreignWrite.StatusCode);

        using var ownerClient = await LoginAsync(data.Teacher);
        using var csrf = await PostAsync(ownerClient, data, Selection(data), null);
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        AssertUnchanged(before, await ReadAsync(data));
    }

    private async Task<WriteOutcome> SaveAsync(SeedData data, UpdateAssessmentQuestionsRequest input)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentQuestionService>()
            .SaveAsync(data.Teacher.Id, data.AssessmentId, input);
    }

    private async Task<AssessmentEntity> ReadAsync(SeedData data)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Assessments
            .AsNoTracking().AsSplitQuery().Include(a => a.Questions).Include(a => a.Attempts)
            .SingleAsync(a => a.Id == data.AssessmentId);
    }

    private static UpdateAssessmentQuestionsRequest Selection(SeedData data, DateTimeOffset? version = null)
    {
        return new UpdateAssessmentQuestionsRequest
        {
            ExpectedUpdatedAtUtc = version ?? InitialVersion,
            Items = new List<AssessmentQuestionInput>
            {
                new AssessmentQuestionInput { QuestionId = data.SecondId, Points = 1.25m },
                new AssessmentQuestionInput { QuestionId = data.FirstId, Points = 2.5m }
            }
        };
    }

    private static List<AssessmentQuestion> Ordered(AssessmentEntity assessment)
    {
        return assessment.Questions.OrderBy(q => q.OrderIndex).ToList();
    }

    private static void AssertUnchanged(AssessmentEntity before, AssessmentEntity after)
    {
        Assert.Equal(before.Title, after.Title);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
        Assert.Equal(before.CourseId, after.CourseId);
        Assert.Equal(Ordered(before).Select(q => (q.Id, q.QuestionId, q.OrderIndex, q.Points)),
            Ordered(after).Select(q => (q.Id, q.QuestionId, q.OrderIndex, q.Points)));
        Assert.Equal(before.Attempts.OrderBy(a => a.Id).Select(a => a.Id),
            after.Attempts.OrderBy(a => a.Id).Select(a => a.Id));
    }

    private static void AssertValidation(WriteOutcome result, string message)
    {
        Assert.Equal(LearningAccessResult.Allowed, result.Access);
        Assert.False(result.Success);
        Assert.Contains(result.Errors.SelectMany(error => error.Value),
            error => error.Contains(message, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<HttpClient> LoginAsync(ApplicationUser user)
    {
        var client = IdentityTestHelpers.CreateClient(_factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    private static string Path(SeedData data)
    {
        return "/TeacherAssessmentQuestions/Index/" + data.AssessmentId;
    }

    private static string SelectedList(string html)
    {
        var match = Regex.Match(html, "<ol\\b[^>]*data-selected-list[^>]*>.*?</ol>", RegexOptions.Singleline);
        Assert.True(match.Success);
        return match.Value;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, SeedData data,
        UpdateAssessmentQuestionsRequest input, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "/TeacherAssessmentQuestions/Save/" + data.AssessmentId);
        request.Content = JsonContent.Create(input);
        if (token != null) request.Headers.Add("RequestVerificationToken", token);
        return await client.SendAsync(request);
    }

    private async Task<SeedData> SeedAsync()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = NewQuestion(teacher.Id, "TASK2_FIRST", EnglishSkill.Reading);
        var second = NewQuestion(teacher.Id, "TASK2_SECOND", EnglishSkill.Reading);
        var stimulus = new Stimulus
        {
            OwnerTeacherUserId = teacher.Id, StimulusType = StimulusType.Passage,
            Title = "Test passage", ContentText = "Passage for the first question."
        };
        first.Stimulus = stimulus;
        var assessment = new AssessmentEntity
        {
            OwnerTeacherUserId = teacher.Id, Title = "Task 2 draft",
            AssessmentType = AssessmentType.PracticeExam, DurationMinutes = 20,
            MaxAttempts = 3, UpdatedAtUtc = InitialVersion,
            Questions = new List<AssessmentQuestion>
            {
                new AssessmentQuestion { Question = first, Points = 3m, OrderIndex = 0 }
            }
        };
        db.Assessments.Add(assessment);
        db.Questions.Add(second);
        await db.SaveChangesAsync();
        return new SeedData(teacher, assessment.Id, first.Id, second.Id, stimulus.Id);
    }

    private static Question NewQuestion(Guid ownerId, string content, EnglishSkill skill)
    {
        return new Question
        {
            OwnerTeacherUserId = ownerId, Content = content, PrimarySkill = skill,
            Level = "Beginner",
            Options = new List<QuestionOption>
            {
                new QuestionOption { Content = "Correct", IsCorrect = true, OrderIndex = 0 },
                new QuestionOption { Content = "Wrong", OrderIndex = 1 }
            }
        };
    }

    private sealed record SeedData(ApplicationUser Teacher, Guid AssessmentId,
        Guid FirstId, Guid SecondId, Guid StimulusId);

    // Ép lỗi CHỈ trong test, sau bước xóa liên kết cũ và trước khi ghi liên kết mới.
    private sealed class RejectNewLinksInterceptor : SaveChangesInterceptor
    {
        public bool SawOldDeletion { get; private set; }
        public bool RejectedInsertion { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var entries = eventData.Context!.ChangeTracker.Entries<AssessmentQuestion>().ToList();
            if (entries.Any(entry => entry.State == EntityState.Deleted)) SawOldDeletion = true;
            if (entries.Any(entry => entry.State == EntityState.Added))
            {
                RejectedInsertion = true;
                throw new InvalidOperationException("TEST_FAILURE_BEFORE_NEW_ASSESSMENT_LINKS");
            }
            return new ValueTask<InterceptionResult<int>>(result);
        }
    }
}
