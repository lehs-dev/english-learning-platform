using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Application.Learning;
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

// Chạy trên SQL Server test riêng, không ghi vào database demo.
public sealed class TeacherAssessmentPublishTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly DateTimeOffset InitialVersion =
        new DateTimeOffset(2026, 10, 19, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OldLock = InitialVersion.AddDays(-1);
    private readonly IntegrationTestFactory _factory;

    public TeacherAssessmentPublishTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(AssessmentType.PlacementTest, false)]
    [InlineData(AssessmentType.SkillAssessment, false)]
    [InlineData(AssessmentType.PracticeExam, true)]
    public async Task Publish_ValidDefinition_LocksContentWithoutChangingDefinitionOrCreatingAttempts(
        AssessmentType type, bool final)
    {
        var data = await SeedAsync(type, final);
        var before = await ReadAsync(data);

        Assert.True((await PublishAsync(data)).Success);

        var after = await ReadAsync(data);
        Assert.Equal(AssessmentStatus.Published, after.Status);
        Assert.NotNull(after.UpdatedAtUtc);
        Assert.NotEqual(InitialVersion, after.UpdatedAtUtc);
        Assert.Equal(Definition(before), Definition(after));
        Assert.Empty(after.Attempts);
        foreach (var item in after.Questions)
        {
            // Dấu khóa cũ được giữ; các nội dung mới dùng cùng thời điểm Publish.
            Assert.Equal(item.OrderIndex == 0 ? OldLock : after.UpdatedAtUtc,
                item.Question.ContentLockedAtUtc);
            if (item.Question.Stimulus != null)
            {
                Assert.Equal(item.OrderIndex == 1 ? after.UpdatedAtUtc : OldLock,
                    item.Question.Stimulus.ContentLockedAtUtc);
            }
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(final ? data.Assessment.Id : (Guid?)null,
            after.CourseId.HasValue
                ? await db.Courses.Where(c => c.Id == after.CourseId).Select(c => c.FinalAssessmentId).SingleAsync()
                : null);

        // Kiểm tra nối với luồng Student hiện có, không sửa service Start.
        var student = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);
        if (after.CourseId.HasValue)
        {
            db.Enrollments.Add(new Enrollment { CourseId = after.CourseId.Value, StudentUserId = student.Id });
            await db.SaveChangesAsync();
        }
        var start = await scope.ServiceProvider.GetRequiredService<IAttemptService>()
            .StartAsync(student.Id, after.Id);
        Assert.Equal(AttemptStartStatus.Started, start.Status);
        Assert.Single((await ReadAsync(data)).Attempts);
    }

    [Theory]
    [InlineData(AssessmentType.SkillAssessment)]
    [InlineData(AssessmentType.PracticeExam)]
    public async Task OptionalPassingScore_RemainsNullThroughSavePreviewPublishAndStudentResult(
        AssessmentType type)
    {
        var data = await SeedAsync(type);
        using var scope = _factory.Services.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentDraftService>();
        var editor = await drafts.GetDraftAsync(data.Teacher.Id, data.Assessment.Id);
        Assert.Equal(LearningAccessResult.Allowed, editor.Access);
        var input = Assert.IsType<AssessmentDraftRequest>(editor.Value);

        // Để trống PassingScore phải được lưu thật, không tự điền 0 hoặc ngưỡng mặc định.
        input.PassingScore = null;
        Assert.True((await drafts.SaveDraftAsync(data.Teacher.Id, data.Assessment.Id, input)).Success);
        var saved = await ReadAsync(data);
        Assert.Null(saved.PassingScore);
        Assert.Equal(type, saved.AssessmentType);
        Assert.Empty(saved.Attempts);

        var previews = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentPreviewService>();
        var draftPreview = await previews.GetAsync(data.Teacher.Id, saved.Id);
        Assert.Equal(LearningAccessResult.Allowed, draftPreview.Access);
        Assert.NotNull(draftPreview.Value);
        Assert.Null(draftPreview.Value.PassingScore);
        Assert.Empty(draftPreview.Value.Warnings);

        var publish = await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentPublishService>()
            .PublishAsync(data.Teacher.Id, saved.Id, saved.UpdatedAtUtc);
        Assert.True(publish.Success);
        var publishedPreview = await previews.GetAsync(data.Teacher.Id, saved.Id);
        Assert.NotNull(publishedPreview.Value);
        Assert.Equal(AssessmentStatus.Published, publishedPreview.Value.Status);
        Assert.Null(publishedPreview.Value.PassingScore);
        Assert.Empty(publishedPreview.Value.Warnings);
        Assert.Empty((await ReadAsync(data)).Attempts);

        var student = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);
        var attempts = scope.ServiceProvider.GetRequiredService<IAttemptService>();
        var started = await attempts.StartAsync(student.Id, saved.Id);
        Assert.Equal(AttemptStartStatus.Started, started.Status);
        Assert.NotNull(started.AttemptId);

        // Cố tình nộp trắng được 0 điểm: không có ngưỡng thì vẫn không được gắn nhãn trượt.
        var finalized = await attempts.FinalizeAsync(student.Id, started.AttemptId.Value);
        Assert.Equal(AttemptFinalizeStatus.Graded, finalized.Status);
        Assert.Equal(0m, finalized.OverallScore);
        Assert.Null(finalized.Passed);
        var after = await ReadAsync(data);
        var attempt = Assert.Single(after.Attempts);
        Assert.Equal(AttemptStatus.Graded, attempt.Status);
        Assert.Null(attempt.Passed);
        Assert.Null(after.PassingScore);
        Assert.Equal(Definition(saved), Definition(after));

        var result = await scope.ServiceProvider.GetRequiredService<IAssessmentQueryService>()
            .GetAttemptResultAsync(student.Id, attempt.Id);
        Assert.NotNull(result);
        Assert.Null(result.PassingScore);
        using var client = await LoginAsync(student);
        using var page = await client.GetAsync("/Assessment/Result/" + attempt.Id);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        string html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        Assert.Contains("không áp dụng ngưỡng đạt / trượt", html);
        Assert.DoesNotContain("assessment-passed", html);
        Assert.DoesNotContain("assessment-failed", html);
    }

    [Theory]
    [InlineData("LastQuestionInvalid", "đúng một đáp án")]
    [InlineData("ArchivedQuestion", "Archived")]
    [InlineData("ForeignFinalReference", "đúng Course")]
    public async Task Publish_InvalidDefinition_DoesNotPartiallyLockOrPublish(string fault, string message)
    {
        var data = await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Guid lastId = data.Assessment.Questions.Single(q => q.OrderIndex == 4).QuestionId;
            if (fault == "LastQuestionInvalid")
                await db.QuestionOptions.Where(o => o.QuestionId == lastId)
                    .ExecuteUpdateAsync(set => set.SetProperty(o => o.IsCorrect, false));
            if (fault == "ArchivedQuestion")
                await db.Questions.Where(q => q.Id == lastId)
                    .ExecuteUpdateAsync(set => set.SetProperty(q => q.Status, QuestionStatus.Archived));
            if (fault == "ForeignFinalReference")
            {
                var other = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
                db.Courses.Add(new Course
                {
                    OwnerTeacherUserId = other.Id, Title = "Foreign final course",
                    FinalAssessmentId = data.Assessment.Id
                });
                await db.SaveChangesAsync();
            }
        }
        string before = Snapshot(await ReadAsync(data));

        AssertValidation(await PublishAsync(data), message);

        Assert.Equal(before, Snapshot(await ReadAsync(data)));
    }

    [Theory]
    [InlineData("StaleVersion", "tải lại")]
    [InlineData("HasAttempt", "đã có Attempt")]
    [InlineData("ArchivedAssessment", "trạng thái Draft")]
    public async Task Publish_StaleVersionOrHistoryOrNonDraft_IsRejectedWithoutWrites(string fault, string message)
    {
        var data = await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (fault == "StaleVersion")
                await db.Assessments.Where(a => a.Id == data.Assessment.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(a => a.UpdatedAtUtc, InitialVersion.AddMinutes(1)));
            if (fault == "ArchivedAssessment")
                await db.Assessments.Where(a => a.Id == data.Assessment.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, AssessmentStatus.Archived));
            if (fault == "HasAttempt")
            {
                var student = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);
                db.Attempts.Add(new Attempt
                {
                    AssessmentId = data.Assessment.Id, StudentUserId = student.Id,
                    DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(20)
                });
                await db.SaveChangesAsync();
            }
        }
        string before = Snapshot(await ReadAsync(data));

        AssertValidation(await PublishAsync(data), message);

        Assert.Equal(before, Snapshot(await ReadAsync(data)));
    }

    [Theory]
    [InlineData("OtherTeacher", LearningAccessResult.NotFound)]
    [InlineData("Student", LearningAccessResult.Forbidden)]
    [InlineData("LockedTeacher", LearningAccessResult.Forbidden)]
    public async Task Publish_ForeignOrUnauthorizedActor_IsRejected(string actorKind, LearningAccessResult expected)
    {
        var data = await SeedAsync();
        Guid actorId = data.Teacher.Id;
        using var scope = _factory.Services.CreateScope();
        if (actorKind == "OtherTeacher" || actorKind == "Student")
            actorId = (await IdentityTestHelpers.CreateUserAsync(_factory,
                actorKind == "Student" ? AppRoles.Student : AppRoles.Teacher)).Id;
        else
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Id == actorId)
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.AccountStatus, AccountStatus.Locked));
        string before = Snapshot(await ReadAsync(data));

        var result = await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentPublishService>()
            .PublishAsync(actorId, data.Assessment.Id, InitialVersion);

        Assert.Equal(expected, result.Access);
        Assert.False(result.Success);
        Assert.Equal(before, Snapshot(await ReadAsync(data)));
    }

    [Fact]
    public async Task Publish_TwoConcurrentRequests_OnlyOneChangesTheDraft()
    {
        var data = await SeedAsync();

        // Mỗi lời gọi mở scope/connection riêng, giống hai request từ hai tab.
        var results = await Task.WhenAll(PublishAsync(data), PublishAsync(data));

        Assert.Single(results, result => result.Success);
        Assert.Single(results, result => !result.Success);
        Assert.All(results, result => Assert.Equal(LearningAccessResult.Allowed, result.Access));
        var after = await ReadAsync(data);
        Assert.Equal(AssessmentStatus.Published, after.Status);
        Assert.Equal(5, after.Questions.Count);
        Assert.All(after.Questions, item => Assert.NotNull(item.Question.ContentLockedAtUtc));
        Assert.Empty(after.Attempts);
    }

    [Fact]
    public async Task Publish_FailureAfterSqlWrites_RollsBackStatusVersionAndNewLocks()
    {
        var data = await SeedAsync();
        string before = Snapshot(await ReadAsync(data));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var interceptor = new FailAfterPublishWrites(data.Assessment.Id);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(db.Database.GetConnectionString()).AddInterceptors(interceptor).Options;
        await using var serviceDb = new AppDbContext(options);
        var service = new TeacherAssessmentPublishService(serviceDb,
            scope.ServiceProvider.GetRequiredService<IAssessmentDefinitionValidator>(),
            NullLogger<TeacherAssessmentPublishService>.Instance,
            scope.ServiceProvider.GetRequiredService<IStimulusAudioStore>());

        var result = await service.PublishAsync(data.Teacher.Id, data.Assessment.Id, InitialVersion);

        Assert.True(interceptor.SawPublishedAndNewLockInSql);
        AssertValidation(result, "Chưa thể Publish");
        Assert.Null(serviceDb.Database.CurrentTransaction);
        // Context mới đọc dữ liệu đã commit, không dùng entity cache sau lỗi.
        Assert.Equal(before, Snapshot(await ReadAsync(data)));
    }

    [Fact]
    public async Task Http_PublishForm_ShowsValidationProtectsCsrfAndPublishesOnlySavedData()
    {
        var data = await SeedAsync();
        using var client = await LoginAsync(data.Teacher);
        using var page = await client.GetAsync(EditPath(data));
        string html = await page.Content.ReadAsStringAsync();
        var form = Regex.Match(html, "<form\\b[^>]*action=\"[^\"]*/Publish[^\"]*\"[^>]*>.*?</form>", RegexOptions.Singleline);
        Assert.True(form.Success, "Publish phải có form riêng, không lồng trong form cấu hình.");
        Assert.DoesNotContain("Input.Title", form.Value);
        var version = Regex.Match(form.Value, "name=\"expectedUpdatedAtUtc\"[^>]*value=\"([^\"]*)\"");
        Assert.True(version.Success);
        var fields = new Dictionary<string, string>
        {
            ["expectedUpdatedAtUtc"] = WebUtility.HtmlDecode(version.Groups[1].Value),
            // Các trường thừa không được dùng để đổi cấu hình hoặc owner khi Publish.
            ["Input.Title"] = "UNSAVED_TITLE_MUST_NOT_BE_USED",
            ["OwnerTeacherUserId"] = Guid.NewGuid().ToString()
        };
        string before = Snapshot(await ReadAsync(data));
        using var missingCsrf = await client.PostAsync(PublishPath(data), new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        Assert.Equal(before, Snapshot(await ReadAsync(data)));

        fields["__RequestVerificationToken"] = await IdentityTestHelpers.GetTokenAsync(client, EditPath(data));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Draft được phép chưa đủ 5 câu; Publish phải thông báo rõ và giữ nguyên Draft.
            await db.AssessmentQuestions.Where(q => q.AssessmentId == data.Assessment.Id && q.OrderIndex == 4)
                .ExecuteDeleteAsync();
        }
        before = Snapshot(await ReadAsync(data));
        using var invalid = await client.PostAsync(PublishPath(data), new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("ít nhất 5 câu hỏi khác nhau", WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync()));
        Assert.Equal(before, Snapshot(await ReadAsync(data)));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fifth = data.Assessment.Questions.Single(q => q.OrderIndex == 4);
            db.AssessmentQuestions.Add(new AssessmentQuestion
            {
                AssessmentId = data.Assessment.Id, QuestionId = fifth.QuestionId,
                OrderIndex = 4, Points = fifth.Points
            });
            await db.SaveChangesAsync();
        }
        using var published = await client.PostAsync(PublishPath(data), new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        Assert.Equal("/TeacherAssessments", published.Headers.Location!.ToString());
        var after = await ReadAsync(data);
        Assert.Equal(AssessmentStatus.Published, after.Status);
        Assert.Equal(data.Assessment.Title, after.Title);
        Assert.Equal(data.Teacher.Id, after.OwnerTeacherUserId);
        Assert.Empty(after.Attempts);

        // Gửi lại form cũ không Publish lần hai hoặc đổi thời điểm khóa.
        before = Snapshot(after);
        using var repeated = await client.PostAsync(PublishPath(data), new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, repeated.StatusCode);
        Assert.Equal(before, Snapshot(await ReadAsync(data)));

        // Preview đã Publish vẫn chỉ đọc, kể cả tải lại nhiều lần.
        before = Snapshot(after);
        for (int index = 0; index < 2; index++)
        {
            using var preview = await client.GetAsync("/TeacherAssessmentPreview/Index/" + after.Id);
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        }
        Assert.Equal(before, Snapshot(await ReadAsync(data)));
    }

    [Fact]
    public async Task Http_GetAndForeignTeacher_CannotPublish()
    {
        var data = await SeedAsync();
        string before = Snapshot(await ReadAsync(data));
        using var owner = await LoginAsync(data.Teacher);
        using var get = await owner.GetAsync(PublishPath(data));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);

        var other = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var client = await LoginAsync(other);
        string token = await IdentityTestHelpers.GetTokenAsync(client, "/TeacherAssessments/Edit");
        using var post = await client.PostAsync(PublishPath(data), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["expectedUpdatedAtUtc"] = InitialVersion.ToString("O"), ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(before, Snapshot(await ReadAsync(data)));
    }

    private async Task<WriteOutcome> PublishAsync(SeedData data)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentPublishService>()
            .PublishAsync(data.Teacher.Id, data.Assessment.Id, InitialVersion);
    }

    private async Task<AssessmentEntity> ReadAsync(SeedData data)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Assessments
            .AsNoTracking().AsSplitQuery()
            .Include(a => a.Questions).ThenInclude(q => q.Question).ThenInclude(q => q.Options)
            .Include(a => a.Questions).ThenInclude(q => q.Question).ThenInclude(q => q.Stimulus)
            .Include(a => a.Attempts).ThenInclude(a => a.Answers)
            .Include(a => a.Attempts).ThenInclude(a => a.SkillResults)
            .SingleAsync(a => a.Id == data.Assessment.Id);
    }

    // Không serialize navigation trực tiếp vì entity có thể tham chiếu vòng.
    private static string Definition(AssessmentEntity assessment)
    {
        return JsonSerializer.Serialize(new
        {
            assessment.Id, assessment.OwnerTeacherUserId, assessment.Title, assessment.Description,
            assessment.AssessmentType, assessment.TargetSkill, assessment.CourseId,
            assessment.DurationMinutes, assessment.MaxAttempts, assessment.PassingScore,
            Questions = assessment.Questions.OrderBy(q => q.OrderIndex).Select(q => new
            {
                q.Id, q.QuestionId, q.OrderIndex, q.Points, q.Question.Content, q.Question.PrimarySkill,
                q.Question.Level, q.Question.Explanation, q.Question.Status,
                q.Question.OwnerTeacherUserId, q.Question.StimulusId,
                Options = q.Question.Options.OrderBy(o => o.OrderIndex).Select(o => new { o.Id, o.Content, o.OrderIndex, o.IsCorrect }),
                Stimulus = q.Question.Stimulus == null ? null : new
                {
                    q.Question.Stimulus.Id, q.Question.Stimulus.OwnerTeacherUserId,
                    q.Question.Stimulus.StimulusType, q.Question.Stimulus.ContentText, q.Question.Stimulus.ResourceUrl
                }
            })
        });
    }

    private static string Snapshot(AssessmentEntity assessment)
    {
        return JsonSerializer.Serialize(new
        {
            Definition = Definition(assessment), assessment.Status, assessment.UpdatedAtUtc,
            Locks = assessment.Questions.OrderBy(q => q.OrderIndex).Select(q => new
            {
                q.Question.ContentLockedAtUtc, StimulusLock = q.Question.Stimulus?.ContentLockedAtUtc
            }),
            Attempts = assessment.Attempts.OrderBy(a => a.Id).Select(a => new
            {
                a.Id, a.Status, a.DeadlineUtc, a.OverallScore, a.FinalizedAtUtc,
                AnswerCount = a.Answers.Count, ResultCount = a.SkillResults.Count
            })
        });
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

    private static string EditPath(SeedData data) => "/TeacherAssessments/Edit/" + data.Assessment.Id;
    private static string PublishPath(SeedData data) => "/TeacherAssessments/Publish/" + data.Assessment.Id;

    private async Task<SeedData> SeedAsync(AssessmentType type = AssessmentType.PracticeExam, bool final = false)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passage = new Stimulus
        {
            OwnerTeacherUserId = teacher.Id, StimulusType = StimulusType.Passage,
            ContentText = "A passage used by two questions."
        };
        passage.LockContent(OldLock);
        var audio = new Stimulus
        {
            OwnerTeacherUserId = teacher.Id, StimulusType = StimulusType.Audio,
            ResourceUrl = await scope.ServiceProvider.GetRequiredService<IStimulusAudioStore>().StoreAsync(StimulusAudioTestData.Wav())
        };
        var assessment = new AssessmentEntity
        {
            OwnerTeacherUserId = teacher.Id, Title = "Task 4 saved assessment",
            AssessmentType = type, TargetSkill = type == AssessmentType.SkillAssessment ? EnglishSkill.Reading : null,
            DurationMinutes = 20, MaxAttempts = 3,
            PassingScore = type == AssessmentType.PlacementTest ? null : 70m,
            UpdatedAtUtc = InitialVersion,
            Course = final ? new Course
            {
                OwnerTeacherUserId = teacher.Id, Title = "Task 4 course", Status = CourseStatus.Published
            } : null
        };
        for (int index = 0; index < 5; index++)
        {
            var question = new Question
            {
                OwnerTeacherUserId = teacher.Id, Content = "TASK4_QUESTION_" + index,
                PrimarySkill = index == 1 && type != AssessmentType.SkillAssessment ? EnglishSkill.Listening : EnglishSkill.Reading,
                Level = "Beginner", Explanation = "A saved explanation.",
                Stimulus = index == 0 || index == 2 ? passage : index == 1 ? audio : null,
                Options = new List<QuestionOption>
                {
                    new QuestionOption { Content = "Correct", OrderIndex = 0, IsCorrect = true },
                    new QuestionOption { Content = "Wrong", OrderIndex = 1 }
                }
            };
            if (index == 0) question.LockContent(OldLock);
            assessment.Questions.Add(new AssessmentQuestion { Question = question, OrderIndex = index, Points = index + 0.25m });
        }
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();
        if (final)
        {
            // Lưu sau Assessment để không tạo vòng phụ thuộc hai FK khi insert.
            assessment.Course!.FinalAssessmentId = assessment.Id;
            await db.SaveChangesAsync();
        }
        return new SeedData(teacher, assessment);
    }

    private sealed record SeedData(ApplicationUser Teacher, AssessmentEntity Assessment);

    // Ép lỗi sau khi SQL đã ghi nhưng trước Commit, chỉ dùng trong test rollback.
    private sealed class FailAfterPublishWrites(Guid assessmentId) : SaveChangesInterceptor
    {
        public bool SawPublishedAndNewLockInSql { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var db = (AppDbContext)eventData.Context!;
            bool published = await db.Assessments.AsNoTracking()
                .AnyAsync(a => a.Id == assessmentId && a.Status == AssessmentStatus.Published, cancellationToken);
            bool newLock = await db.AssessmentQuestions.AsNoTracking()
                .AnyAsync(q => q.AssessmentId == assessmentId && q.OrderIndex == 4
                    && q.Question.ContentLockedAtUtc != null, cancellationToken);
            SawPublishedAndNewLockInSql = published && newLock;
            throw new InvalidOperationException("TEST_FAIL_AFTER_PUBLISH_SQL_BEFORE_COMMIT");
        }
    }
}
