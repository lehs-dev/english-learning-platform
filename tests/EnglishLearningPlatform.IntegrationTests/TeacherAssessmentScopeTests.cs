using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.AssessmentAttempts;
using EnglishLearningPlatform.Infrastructure.AssessmentAuthoring;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using AssessmentEntity = EnglishLearningPlatform.Domain.Entities.Assessment;

namespace EnglishLearningPlatform.IntegrationTests;

// Mọi dữ liệu dưới đây nằm trong SQL Server test riêng của factory.
public sealed class TeacherAssessmentScopeTests(IntegrationTestFactory factory)
    : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData(AssessmentStatus.Draft)]
    [InlineData(AssessmentStatus.Published)]
    [InlineData(AssessmentStatus.Unpublished)]
    public async Task BeforeFirstAttempt_AttachSwitchAndDetach_PreserveDefinitionAndLocks(AssessmentStatus status)
    {
        var data = await SeedAsync(status);
        string definition = await DefinitionAsync(data);
        foreach (Guid? target in new Guid?[] { data.First.Id, data.Second.Id, null })
        {
            var before = await ReadAsync(data);
            var editor = await EditorAsync(data);
            Assert.True(editor.CanChangeScope);
            Assert.False(editor.HasAttempt);
            Assert.Equal(before.CourseId, editor.CourseId);
            Assert.Equal(new[] { data.First.Id, data.Second.Id }.Order(), editor.Courses.Select(c => c.Id).Order());

            Assert.True((await SaveAsync(data, Request(before, target))).Success);

            var after = await ReadAsync(data);
            Assert.Equal(target, after.CourseId);
            Assert.Equal(status, after.Status);
            Assert.NotEqual(before.UpdatedAtUtc, after.UpdatedAtUtc);
            Assert.Equal(definition, await DefinitionAsync(data));
            var reloaded = await EditorAsync(data);
            Assert.Equal(after.UpdatedAtUtc, reloaded.UpdatedAtUtc);
            Assert.Equal(target, reloaded.CourseId);
            Assert.Equal(target == data.First.Id ? data.First.Title : target == data.Second.Id ? data.Second.Title : null,
                reloaded.CourseTitle);
        }
        // Chọn lại đúng phạm vi không phải một lần chỉnh sửa nội dung.
        var unchanged = await ReadAsync(data);
        Assert.True((await SaveAsync(data, Request(unchanged, null))).Success);
        Assert.Equal(unchanged.UpdatedAtUtc, (await ReadAsync(data)).UpdatedAtUtc);
    }

    [Theory]
    [InlineData("ForeignCourse")]
    [InlineData("MissingCourse")]
    [InlineData("ArchivedCourse")]
    [InlineData("ArchivedAssessment")]
    [InlineData("InvalidPublished")]
    public async Task InvalidScopeOrPublishedDefinition_LeavesOldScopeVersionAndQuestionsUntouched(string fault)
    {
        var data = await SeedAsync(fault == "ArchivedAssessment" ? AssessmentStatus.Archived : AssessmentStatus.Published);
        Guid? target = data.First.Id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (fault == "ForeignCourse") target = data.Foreign.Id;
            if (fault == "MissingCourse") target = Guid.NewGuid();
            if (fault == "ArchivedCourse")
                await db.Courses.Where(c => c.Id == data.First.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, CourseStatus.Archived));
            if (fault == "InvalidPublished")
                await db.QuestionOptions.Where(o => o.QuestionId == data.Assessment.Questions.First().QuestionId)
                    .ExecuteUpdateAsync(set => set.SetProperty(o => o.IsCorrect, false));
        }
        var before = await ReadAsync(data);
        string definition = await DefinitionAsync(data);

        var result = await SaveAsync(data, Request(before, target));

        Assert.False(result.Success);
        Assert.Equal(before.CourseId, (await ReadAsync(data)).CourseId);
        Assert.Equal(before.UpdatedAtUtc, (await ReadAsync(data)).UpdatedAtUtc);
        Assert.Equal(definition, await DefinitionAsync(data));
        if (fault == "ArchivedCourse")
            Assert.DoesNotContain((await EditorAsync(data)).Courses, c => c.Id == data.First.Id);
    }

    [Fact]
    public async Task StaleEditorAndFinalReference_CannotOverwriteOrSilentlyDetachFinal()
    {
        var data = await SeedAsync();
        var stale = Request(await ReadAsync(data), data.Second.Id);
        Assert.True((await SaveAsync(data, Request(await ReadAsync(data), data.First.Id))).Success);
        var before = await ReadAsync(data);
        AssertRejected(await SaveAsync(data, stale));
        Assert.Equal(before.UpdatedAtUtc, (await ReadAsync(data)).UpdatedAtUtc);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Courses.Where(c => c.Id == data.First.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.FinalAssessmentId, (Guid?)data.Assessment.Id));
        }
        var editor = await EditorAsync(data);
        Assert.True(editor.IsFinal);
        Assert.False(editor.CanChangeScope);
        foreach (Guid? target in new Guid?[] { null, data.Second.Id })
            AssertRejected(await SaveAsync(data, Request(before, target)));
        var after = await ReadAsync(data);
        Assert.Equal(data.First.Id, after.CourseId);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
        using var verify = factory.Services.CreateScope();
        Assert.Equal(data.Assessment.Id, await verify.ServiceProvider.GetRequiredService<AppDbContext>().Courses
            .Where(c => c.Id == data.First.Id).Select(c => c.FinalAssessmentId).SingleAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstAttempt_InProgressOrGraded_LocksScopeButOwnerCanReadIt(bool graded)
    {
        var data = await SeedAsync();
        var input = Request(await ReadAsync(data), data.First.Id);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var start = await StartAsync(student.Id, data.Assessment.Id);
        Assert.Equal(AttemptStartStatus.Started, start.Status);
        if (graded)
        {
            using var scope = factory.Services.CreateScope();
            Assert.Equal(AttemptFinalizeStatus.Graded, (await scope.ServiceProvider.GetRequiredService<IAttemptService>()
                .FinalizeAsync(student.Id, start.AttemptId!.Value)).Status);
        }
        var before = await ReadAsync(data);
        var editor = await EditorAsync(data);
        Assert.True(editor.HasAttempt);
        Assert.False(editor.CanChangeScope);
        Assert.False(string.IsNullOrWhiteSpace(editor.LockReason));
        Assert.False((await SaveAsync(data, input)).Success);
        // Cả no-op cũng không vượt qua việc kiểm tra Attempt/version ở server.
        Assert.False((await SaveAsync(data, Request(before, null))).Success);
        Assert.Null((await ReadAsync(data)).CourseId);
        Assert.Equal(before.UpdatedAtUtc, (await ReadAsync(data)).UpdatedAtUtc);
        using var verify = factory.Services.CreateScope();
        var attempt = await verify.ServiceProvider.GetRequiredService<AppDbContext>().Attempts.AsNoTracking()
            .SingleAsync(a => a.AssessmentId == data.Assessment.Id);
        Assert.Equal(start.AttemptId, attempt.Id);
        Assert.Equal(start.DeadlineUtc, attempt.DeadlineUtc);
        Assert.Equal(graded ? AttemptStatus.Graded : AttemptStatus.InProgress, attempt.Status);
    }

    [Fact]
    public async Task ForeignTeacherStudentAndDisabledOwner_CannotReadOrWriteScope()
    {
        var data = await SeedAsync();
        var input = Request(await ReadAsync(data), data.First.Id);
        foreach (string role in new[] { AppRoles.Teacher, AppRoles.Student })
        {
            var actor = await IdentityTestHelpers.CreateUserAsync(factory, role);
            using var scope = factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentScopeService>();
            var expected = role == AppRoles.Teacher ? LearningAccessResult.NotFound : LearningAccessResult.Forbidden;
            Assert.Equal(expected, (await service.GetEditorAsync(actor.Id, data.Assessment.Id)).Access);
            Assert.Equal(expected, (await service.SaveAsync(actor.Id, data.Assessment.Id, input)).Access);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.Id == data.Teacher.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.AccountStatus, AccountStatus.Disabled));
            var service = scope.ServiceProvider.GetRequiredService<ITeacherAssessmentScopeService>();
            Assert.Equal(LearningAccessResult.Forbidden, (await service.GetEditorAsync(data.Teacher.Id, data.Assessment.Id)).Access);
            Assert.Equal(LearningAccessResult.Forbidden, (await service.SaveAsync(data.Teacher.Id, data.Assessment.Id, input)).Access);
        }
        Assert.Null((await ReadAsync(data)).CourseId);
        Assert.Equal(input.ExpectedUpdatedAtUtc, (await ReadAsync(data)).UpdatedAtUtc);
    }

    [Fact]
    public async Task ScopeTransition_ImmediatelyChangesStudentOverviewAndStartEnrollmentRequirements()
    {
        var data = await SeedAsync();
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        Assert.NotNull(await OverviewAsync(student.Id, data.Assessment.Id));
        Assert.True((await SaveAsync(data, Request(await ReadAsync(data), data.First.Id))).Success);
        Assert.Null(await OverviewAsync(student.Id, data.Assessment.Id));
        Assert.Equal(AttemptStartStatus.Forbidden, (await StartAsync(student.Id, data.Assessment.Id)).Status);

        // Một Enrollment thanh toán Pending có tồn tại vẫn chưa cấp quyền.
        Guid paymentId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = new Order
            {
                CourseId = data.First.Id, StudentUserId = student.Id, Amount = 100000m,
                Currency = "VND", Provider = "scope-test"
            };
            var payment = new Payment
            {
                Order = order, Amount = order.Amount, Status = PaymentStatus.Pending,
                Provider = "scope-test", ProviderTransactionId = Guid.NewGuid().ToString("N")
            };
            db.Enrollments.Add(new Enrollment { CourseId = data.First.Id, StudentUserId = student.Id, Payment = payment });
            await db.SaveChangesAsync();
            paymentId = payment.Id;
        }
        Assert.Null(await OverviewAsync(student.Id, data.Assessment.Id));
        Assert.Equal(AttemptStartStatus.Forbidden, (await StartAsync(student.Id, data.Assessment.Id)).Status);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Payments.Where(p => p.Id == paymentId)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, PaymentStatus.Succeeded)
                    .SetProperty(p => p.PaidAtUtc, (DateTimeOffset?)DateTimeOffset.UtcNow));
        Assert.Equal(data.First.Id, (await OverviewAsync(student.Id, data.Assessment.Id))!.CourseId);

        Assert.True((await SaveAsync(data, Request(await ReadAsync(data), data.Second.Id))).Success);
        Assert.Null(await OverviewAsync(student.Id, data.Assessment.Id));
        Assert.Equal(AttemptStartStatus.Forbidden, (await StartAsync(student.Id, data.Assessment.Id)).Status);
        Assert.True((await SaveAsync(data, Request(await ReadAsync(data), null))).Success);
        Assert.Null((await OverviewAsync(student.Id, data.Assessment.Id))!.CourseId);
        Assert.Equal(AttemptStartStatus.Started, (await StartAsync(student.Id, data.Assessment.Id)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentStartAndScopeChange_RecheckLockedState_WithoutTimingDependentSleeps(bool startWins)
    {
        var data = await SeedAsync();
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var input = Request(await ReadAsync(data), data.First.Id);
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var connection = services.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
        AttemptStartResult? started = null;
        WriteOutcome? saved = null;
        // Dừng đúng trước SQL lấy khóa Assessment; request khác chạy hết trên connection riêng.
        // Vì vậy cả hai thứ tự đều tái hiện được, không phụ thuộc tốc độ máy/Task.Delay.
        var gate = new BeforeAssessmentLock(async () =>
        {
            if (startWins) started = await StartAsync(student.Id, data.Assessment.Id);
            else saved = await SaveAsync(data, input);
        });
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).AddInterceptors(gate).Options;
        await using var interceptedDb = new AppDbContext(options);
        if (startWins)
        {
            var service = ActivatorUtilities.CreateInstance<TeacherAssessmentScopeService>(services, interceptedDb);
            saved = await service.SaveAsync(data.Teacher.Id, data.Assessment.Id, input);
        }
        else
        {
            var service = new AssessmentAttemptService(interceptedDb,
                services.GetRequiredService<UserManager<ApplicationUser>>(),
                services.GetRequiredService<IExamScoringService>(),
                services.GetRequiredService<IAssessmentDefinitionValidator>());
            started = await service.StartAsync(student.Id, data.Assessment.Id);
        }
        Assert.True(gate.Applied);
        Assert.NotNull(started);
        Assert.NotNull(saved);
        var after = await ReadAsync(data);
        using var verify = factory.Services.CreateScope();
        var attempts = await verify.ServiceProvider.GetRequiredService<AppDbContext>().Attempts.AsNoTracking()
            .Where(a => a.AssessmentId == data.Assessment.Id).ToListAsync();
        if (startWins)
        {
            Assert.Equal(AttemptStartStatus.Started, started.Status);
            Assert.False(saved.Success);
            Assert.Null(after.CourseId);
            Assert.Equal(started.AttemptId, Assert.Single(attempts).Id);
            Assert.Equal(started.DeadlineUtc, attempts[0].DeadlineUtc);
        }
        else
        {
            Assert.True(saved.Success);
            Assert.Equal(AttemptStartStatus.Unavailable, started.Status);
            Assert.Equal(data.First.Id, after.CourseId);
            Assert.Empty(attempts);
        }
    }

    [Fact]
    public async Task Http_ScopeEditor_ProtectsPostAndOwnership_ReportsStaleInputAndReloadsSavedScope()
    {
        var data = await SeedAsync();
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, data.Teacher);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        string path = "/TeacherAssessmentScope/Edit/" + data.Assessment.Id;
        using var page = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl!.NoStore);
        string html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Input.CourseId", html);
        Assert.Contains(data.First.Id.ToString(), html);
        Assert.DoesNotContain(data.Foreign.Id.ToString(), html);
        var fields = new Dictionary<string, string>
        {
            ["Input.CourseId"] = data.First.Id.ToString(),
            ["Input.ExpectedUpdatedAtUtc"] = FieldValue(html, "Input.ExpectedUpdatedAtUtc"),
            ["Input.OwnerTeacherUserId"] = Guid.NewGuid().ToString(),
            ["Input.Status"] = "Archived"
        };
        using var missingToken = await client.PostAsync(path, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        Assert.Null((await ReadAsync(data)).CourseId);
        fields["__RequestVerificationToken"] = FieldValue(html, "__RequestVerificationToken");
        using var valid = await client.PostAsync(path, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        using var reload = await client.GetAsync(valid.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, reload.StatusCode);
        Assert.Contains(data.First.Title, WebUtility.HtmlDecode(await reload.Content.ReadAsStringAsync()));
        var before = await ReadAsync(data);
        Assert.Equal(data.Teacher.Id, before.OwnerTeacherUserId);
        Assert.Equal(AssessmentStatus.Published, before.Status);
        fields["Input.CourseId"] = data.Second.Id.ToString();
        using var stale = await client.PostAsync(path, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.Contains("tải lại", WebUtility.HtmlDecode(await stale.Content.ReadAsStringAsync()), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before.UpdatedAtUtc, (await ReadAsync(data)).UpdatedAtUtc);
        Assert.Equal(data.First.Id, (await ReadAsync(data)).CourseId);

        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var foreignClient = IdentityTestHelpers.CreateClient(factory);
        using var foreignLogin = await IdentityTestHelpers.LoginAsync(foreignClient, other);
        using var foreignPage = await foreignClient.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, foreignPage.StatusCode);
    }

    private async Task<TeacherAssessmentScopeEditor> EditorAsync(SeedData data)
    {
        using var scope = factory.Services.CreateScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentScopeService>()
            .GetEditorAsync(data.Teacher.Id, data.Assessment.Id);
        Assert.Equal(LearningAccessResult.Allowed, outcome.Access);
        return Assert.IsType<TeacherAssessmentScopeEditor>(outcome.Value);
    }

    private async Task<WriteOutcome> SaveAsync(SeedData data, AssessmentScopeRequest request)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentScopeService>()
            .SaveAsync(data.Teacher.Id, data.Assessment.Id, request);
    }

    private async Task<AttemptStartResult> StartAsync(Guid studentId, Guid assessmentId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAttemptService>().StartAsync(studentId, assessmentId);
    }

    private async Task<AssessmentOverviewModel?> OverviewAsync(Guid studentId, Guid assessmentId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAssessmentQueryService>()
            .GetAssessmentOverviewAsync(studentId, assessmentId);
    }

    private async Task<AssessmentEntity> ReadAsync(SeedData data)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Assessments.AsNoTracking()
            .SingleAsync(a => a.Id == data.Assessment.Id);
    }

    // Chụp riêng định nghĩa và marker để phát hiện scope change vô tình sửa nội dung.
    private async Task<string> DefinitionAsync(SeedData data)
    {
        using var scope = factory.Services.CreateScope();
        var assessment = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Assessments.AsNoTracking()
            .Include(a => a.Questions).ThenInclude(q => q.Question).ThenInclude(q => q.Options)
            .SingleAsync(a => a.Id == data.Assessment.Id);
        return JsonSerializer.Serialize(new
        {
            assessment.Id, assessment.OwnerTeacherUserId, assessment.Title, assessment.Description,
            assessment.AssessmentType, assessment.TargetSkill, assessment.DurationMinutes, assessment.MaxAttempts, assessment.PassingScore,
            Questions = assessment.Questions.OrderBy(q => q.OrderIndex).Select(q => new
            {
                q.Id, q.QuestionId, q.OrderIndex, q.Points, q.Question.Content, q.Question.PrimarySkill,
                q.Question.UpdatedAtUtc, q.Question.ContentLockedAtUtc,
                Options = q.Question.Options.OrderBy(o => o.OrderIndex).Select(o => new { o.Id, o.Content, o.IsCorrect, o.OrderIndex })
            })
        });
    }

    private static AssessmentScopeRequest Request(AssessmentEntity assessment, Guid? target) => new()
    {
        CourseId = target, ExpectedUpdatedAtUtc = assessment.UpdatedAtUtc
    };

    private static void AssertRejected(WriteOutcome result)
    {
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    private static string FieldValue(string html, string name)
    {
        var match = Regex.Match(html, "name=\"" + Regex.Escape(name) + "\"[^>]*value=\"([^\"]*)\"");
        Assert.True(match.Success, "Không tìm thấy field " + name);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task<SeedData> SeedAsync(AssessmentStatus status = AssessmentStatus.Published)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var foreign = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var first = new Course
        {
            OwnerTeacherUserId = teacher.Id, Title = "Scope course A", Status = CourseStatus.Published, IsPaid = true, Price = 100000m
        };
        var second = new Course { OwnerTeacherUserId = teacher.Id, Title = "Scope course B", Status = CourseStatus.Unpublished };
        var foreignCourse = new Course { OwnerTeacherUserId = foreign.Id, Title = "Private foreign course", Status = CourseStatus.Published };
        var assessment = new AssessmentEntity
        {
            OwnerTeacherUserId = teacher.Id, Title = "Scope test assessment", Status = status,
            AssessmentType = AssessmentType.PracticeExam, DurationMinutes = 20, MaxAttempts = 3, PassingScore = 70m,
            UpdatedAtUtc = new DateTimeOffset(2026, 10, 21, 2, 0, 0, TimeSpan.Zero)
        };
        for (int index = 0; index < 5; index++)
        {
            var question = new Question
            {
                OwnerTeacherUserId = teacher.Id, Content = "Scope question " + index, PrimarySkill = EnglishSkill.Grammar,
                Status = QuestionStatus.Published,
                Options = new List<QuestionOption>
                {
                    new QuestionOption { Content = "Correct", IsCorrect = true, OrderIndex = 0 },
                    new QuestionOption { Content = "Wrong", OrderIndex = 1 }
                }
            };
            question.LockContent(assessment.UpdatedAtUtc.Value.AddDays(-1));
            assessment.Questions.Add(new AssessmentQuestion { Question = question, Points = index + 1m, OrderIndex = index });
        }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Courses.AddRange(first, second, foreignCourse);
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();
        return new SeedData(teacher, assessment, first, second, foreignCourse);
    }

    private sealed record SeedData(ApplicationUser Teacher, AssessmentEntity Assessment, Course First, Course Second, Course Foreign);

    private sealed class BeforeAssessmentLock(Func<Task> competingRequest) : DbCommandInterceptor
    {
        public bool Applied { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!Applied && command.CommandText.Contains("FROM Assessments WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal))
            {
                Applied = true;
                await competingRequest();
            }
            return result;
        }
    }
}
