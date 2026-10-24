using System.Data.Common;
using System.Net;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.AssessmentAuthoring;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Chỉ dùng SQL Server test riêng. Không chạy browser hoặc ghi database demo.
public sealed class TeacherCourseFinalTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData(AssessmentType.SkillAssessment)]
    [InlineData(AssessmentType.PracticeExam)]
    public async Task UnusedFinal_CanAssignReplaceAndRemove_WithoutChangingAssessments(AssessmentType type)
    {
        var data = await SeedAsync(type);
        var first = await AssessmentAsync(data.First.Id);
        Assert.True((await SaveAsync(data, data.First.Id)).Success);
        var assigned = await CourseAsync(data);
        Assert.Equal(data.First.Id, assigned.FinalAssessmentId);
        var editor = await EditorAsync(data);
        Assert.True(editor.CanChangeFinal);
        Assert.False(editor.HasFinalAttempts);
        Assert.Equal(2, editor.Assessments.Count);
        Assert.True((await SaveAsync(data, data.Second.Id)).Success);
        Assert.Equal(data.Second.Id, (await CourseAsync(data)).FinalAssessmentId);
        Assert.True((await SaveAsync(data, null)).Success);
        var removed = await CourseAsync(data);
        Assert.Null(removed.FinalAssessmentId);
        Assert.NotEqual(assigned.UpdatedAtUtc, removed.UpdatedAtUtc);
        var unchanged = await AssessmentAsync(data.First.Id);
        Assert.Equal(first.UpdatedAtUtc, unchanged.UpdatedAtUtc);
        Assert.Equal(first.PassingScore, unchanged.PassingScore);
        Assert.Equal(AssessmentStatus.Published, unchanged.Status);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(10, await db.AssessmentQuestions.CountAsync(q => q.AssessmentId == data.First.Id || q.AssessmentId == data.Second.Id));
        Assert.False(await db.Attempts.AnyAsync(a => a.AssessmentId == data.First.Id || a.AssessmentId == data.Second.Id));
    }

    [Theory]
    [InlineData("Placement")]
    [InlineData("Independent")]
    [InlineData("OtherCourse")]
    [InlineData("ForeignOwner")]
    [InlineData("Draft")]
    [InlineData("MissingScore")]
    [InlineData("InvalidMcq")]
    [InlineData("ArchivedCourse")]
    public async Task InvalidCandidate_LeavesExistingFinalVersionAndCompletionUnchanged(string fault)
    {
        var data = await SeedAsync();
        Assert.True((await SaveAsync(data, data.Second.Id)).Success);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var candidate = await db.Assessments.SingleAsync(a => a.Id == data.First.Id);
            if (fault == "Placement") { candidate.AssessmentType = AssessmentType.PlacementTest; candidate.PassingScore = null; }
            if (fault == "Independent") candidate.CourseId = null;
            if (fault == "OtherCourse")
            {
                var other = new Course { OwnerTeacherUserId = data.Teacher.Id, Title = "Other course" };
                db.Courses.Add(other); candidate.CourseId = other.Id;
            }
            if (fault == "ForeignOwner")
                candidate.OwnerTeacherUserId = (await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher)).Id;
            if (fault == "Draft") candidate.Status = AssessmentStatus.Draft;
            if (fault == "MissingScore") candidate.PassingScore = null;
            if (fault == "ArchivedCourse")
                (await db.Courses.SingleAsync(c => c.Id == data.Course.Id)).Status = CourseStatus.Archived;
            if (fault == "InvalidMcq")
                await db.QuestionOptions.Where(o => o.Question.AssessmentQuestions.Any(q => q.AssessmentId == candidate.Id))
                    .ExecuteUpdateAsync(set => set.SetProperty(o => o.IsCorrect, false));
            await db.SaveChangesAsync();
        }
        var before = await CourseAsync(data);
        var rejected = await SaveAsync(data, data.First.Id);
        Assert.False(rejected.Success);
        Assert.NotEmpty(rejected.Errors);
        var after = await CourseAsync(data);
        Assert.Equal(before.FinalAssessmentId, after.FinalAssessmentId);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
        Assert.Null(await CompletedAsync(data));
        if (fault != "InvalidMcq" && fault != "ArchivedCourse")
            Assert.DoesNotContain((await EditorAsync(data)).Assessments, a => a.Id == data.First.Id);
        if (fault == "ArchivedCourse")
            Assert.False((await EditorAsync(data)).CanChangeFinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UsedFinal_CannotRemoveOrReplaceWithAnotherId_EvenAfterEnrollmentRemoved(bool graded)
    {
        var data = await SeedAsync();
        Assert.True((await SaveAsync(data, data.First.Id)).Success);
        var started = await StartAsync(data, data.First.Id);
        Assert.Equal(AttemptStartStatus.Started, started.Status);
        if (graded)
        {
            using var scope = factory.Services.CreateScope();
            Assert.Equal(AttemptFinalizeStatus.Graded, (await scope.ServiceProvider.GetRequiredService<IAttemptService>()
                .FinalizeAsync(data.Student.Id, started.AttemptId!.Value)).Status);
        }
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Enrollments
                .Where(e => e.Id == data.Enrollment.Id).ExecuteDeleteAsync();
        var before = await CourseAsync(data);
        // Một bản clone cũng là ID mới; nó không mở khóa Final hiện tại.
        Assert.False((await SaveAsync(data, data.Second.Id)).Success);
        Assert.False((await SaveAsync(data, null)).Success);
        Assert.Equal(before.FinalAssessmentId, (await CourseAsync(data)).FinalAssessmentId);
        Assert.Equal(before.UpdatedAtUtc, (await CourseAsync(data)).UpdatedAtUtc);
        var editor = await EditorAsync(data);
        Assert.True(editor.HasFinalAttempts);
        Assert.False(editor.CanChangeFinal);
        using var verify = factory.Services.CreateScope();
        Assert.Equal(started.AttemptId, await verify.ServiceProvider.GetRequiredService<AppDbContext>().Attempts
            .Where(a => a.AssessmentId == data.First.Id).Select(a => a.Id).SingleAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemovingUnusedFinal_ReconcilesImmediatelyButRequiresEffectiveLessons(bool effectiveLessons)
    {
        var data = await SeedAsync(completeLessons: true);
        if (!effectiveLessons)
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Modules.Where(m => m.CourseId == data.Course.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.Visibility, ModuleVisibility.Hidden));
        }
        Assert.True((await SaveAsync(data, data.First.Id)).Success);
        Assert.Null(await CompletedAsync(data));
        Assert.True((await SaveAsync(data, null)).Success);
        Assert.Equal(effectiveLessons, (await CompletedAsync(data)).HasValue);
        var historical = await CompletedAsync(data);
        if (historical.HasValue)
        {
            Assert.True((await SaveAsync(data, data.Second.Id)).Success);
            Assert.Equal(historical, await CompletedAsync(data));
            Assert.True((await SaveAsync(data, null)).Success);
            Assert.Equal(historical, await CompletedAsync(data));
        }
    }

    [Fact]
    public async Task ReplacingUnusedFinalWithAlreadyPassedAssessment_CompletesWithoutStudentRequest()
    {
        var data = await SeedAsync(completeLessons: true);
        Assert.True((await SaveAsync(data, data.First.Id)).Success);
        var start = await StartAsync(data, data.Second.Id);
        Assert.Equal(AttemptStartStatus.Started, start.Status);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<IAttemptService>();
            var answers = await db.AssessmentQuestions.Where(q => q.AssessmentId == data.Second.Id)
                .Select(q => new { q.Id, Correct = q.Question.Options.Where(o => o.IsCorrect).Select(o => o.Id).Single() }).ToListAsync();
            foreach (var answer in answers)
                Assert.Equal(AttemptSaveStatus.Saved, await service.SaveAnswerAsync(data.Student.Id, start.AttemptId!.Value, answer.Id, answer.Correct));
            Assert.True((await service.FinalizeAsync(data.Student.Id, start.AttemptId!.Value)).Passed);
        }
        Assert.Null(await CompletedAsync(data)); // Final hiện tại vẫn là First chưa được làm.
        Assert.True((await SaveAsync(data, data.Second.Id)).Success);
        Assert.NotNull(await CompletedAsync(data));
        Assert.False((await EditorAsync(data)).CanChangeFinal);
    }

    [Theory]
    [InlineData(AssessmentStatus.Unpublished)]
    [InlineData(AssessmentStatus.Archived)]
    public async Task Withdrawal_RejectsActiveCourseFinal_ArchivePreservesArchivedCourseHistory(AssessmentStatus target)
    {
        var data = await SeedAsync();
        Assert.True((await SaveAsync(data, data.First.Id)).Success);
        var assessment = await AssessmentAsync(data.First.Id);
        Assert.False((await WithdrawAsync(data, target, assessment.UpdatedAtUtc)).Success);
        Assert.Equal(AssessmentStatus.Published, (await AssessmentAsync(data.First.Id)).Status);

        var started = await StartAsync(data, data.First.Id);
        using (var scope = factory.Services.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<ICourseService>()
                .ChangeStatusAsync(data.Teacher.Id, data.Course.Id, CourseStatus.Archived)).Success);
        // Course Archived chỉ cho Archive Final, không giữ Final lịch sử ở Unpublished.
        var archived = await WithdrawAsync(data, target, assessment.UpdatedAtUtc);
        Assert.Equal(target == AssessmentStatus.Archived, archived.Success);
        Assert.Equal(data.First.Id, (await CourseAsync(data)).FinalAssessmentId);
        using var resume = factory.Services.CreateScope();
        var attempts = resume.ServiceProvider.GetRequiredService<IAttemptService>();
        var continued = await attempts.StartAsync(data.Student.Id, data.First.Id);
        Assert.Equal(AttemptStartStatus.Resumed, continued.Status);
        Assert.Equal(started.AttemptId, continued.AttemptId);
        Assert.Equal(started.DeadlineUtc, continued.DeadlineUtc);
        Assert.Equal(AttemptFinalizeStatus.Graded, (await attempts.FinalizeAsync(data.Student.Id, started.AttemptId!.Value)).Status);
    }

    [Fact]
    public async Task StartWinningBeforeFinalSave_BlocksReplacementUnderCourseLock()
    {
        var data = await SeedAsync();
        Assert.True((await SaveAsync(data, data.First.Id)).Success);
        var before = await CourseAsync(data);
        using var scope = factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
        var gate = new BeforeCourseLock(async () => Assert.Equal(AttemptStartStatus.Started,
            (await StartAsync(data, data.First.Id)).Status));
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).AddInterceptors(gate).Options;
        await using var db = new AppDbContext(options);
        var service = ActivatorUtilities.CreateInstance<TeacherCourseFinalService>(scope.ServiceProvider, db);
        var saved = await service.SaveAsync(data.Teacher.Id, data.Course.Id, Request(before, data.Second.Id));
        Assert.True(gate.Applied);
        Assert.False(saved.Success);
        Assert.Equal(before.FinalAssessmentId, (await CourseAsync(data)).FinalAssessmentId);
        Assert.Equal(before.UpdatedAtUtc, (await CourseAsync(data)).UpdatedAtUtc);
    }

    [Fact]
    public async Task FinalAssignedBeforeWithdrawal_TakesPrecedenceOverOldAssessmentList()
    {
        var data = await SeedAsync();
        var assessment = await AssessmentAsync(data.First.Id);
        using var scope = factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
        var gate = new BeforeCourseLock(async () => Assert.True((await SaveAsync(data, data.First.Id)).Success));
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).AddInterceptors(gate).Options;
        await using var db = new AppDbContext(options);
        var service = ActivatorUtilities.CreateInstance<TeacherAssessmentLifecycleService>(scope.ServiceProvider, db);

        var withdrawn = await service.ChangeStatusAsync(data.Teacher.Id, data.First.Id,
            AssessmentStatus.Unpublished, assessment.UpdatedAtUtc);

        Assert.True(gate.Applied);
        Assert.False(withdrawn.Success);
        Assert.Equal(data.First.Id, (await CourseAsync(data)).FinalAssessmentId);
        Assert.Equal(AssessmentStatus.Published, (await AssessmentAsync(data.First.Id)).Status);
    }

    [Fact]
    public async Task NonFinalWithdrawal_StopsNewStartsButPreservesCurrentAttemptAndVersion()
    {
        var data = await SeedAsync();
        var before = await AssessmentAsync(data.First.Id);
        var started = await StartAsync(data, data.First.Id);
        Assert.Equal(AttemptStartStatus.Started, started.Status);
        Assert.True((await WithdrawAsync(data, AssessmentStatus.Unpublished, before.UpdatedAtUtc)).Success);
        var unpublished = await AssessmentAsync(data.First.Id);
        Assert.Equal(AssessmentStatus.Unpublished, unpublished.Status);
        Assert.NotEqual(before.UpdatedAtUtc, unpublished.UpdatedAtUtc);

        // Tab cũ không được Archive bằng version trước khi Unpublish.
        Assert.False((await WithdrawAsync(data, AssessmentStatus.Archived, before.UpdatedAtUtc)).Success);
        Assert.Equal(AssessmentStatus.Unpublished, (await AssessmentAsync(data.First.Id)).Status);
        var newcomer = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Enrollments.Add(new Enrollment { CourseId = data.Course.Id, StudentUserId = newcomer.Id });
            await db.SaveChangesAsync();
            Assert.NotEqual(AttemptStartStatus.Started, (await scope.ServiceProvider.GetRequiredService<IAttemptService>()
                .StartAsync(newcomer.Id, data.First.Id)).Status);
        }
        Assert.True((await WithdrawAsync(data, AssessmentStatus.Archived, unpublished.UpdatedAtUtc)).Success);
        var resumed = await StartAsync(data, data.First.Id);
        Assert.Equal(AttemptStartStatus.Resumed, resumed.Status);
        Assert.Equal(started.AttemptId, resumed.AttemptId);
        Assert.Equal(started.DeadlineUtc, resumed.DeadlineUtc);
        using var finish = factory.Services.CreateScope();
        Assert.Equal(AttemptFinalizeStatus.Graded, (await finish.ServiceProvider.GetRequiredService<IAttemptService>()
            .FinalizeAsync(data.Student.Id, started.AttemptId!.Value)).Status);
    }

    [Fact]
    public async Task Http_ProtectsOwnershipCsrfAndStaleFinalChoice_AndOffersManagementLink()
    {
        var data = await SeedAsync();
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, data.Teacher);
        string path = "/TeacherCourseFinal/Edit/" + data.Course.Id;
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        string html = await response.Content.ReadAsStringAsync();
        var fields = new Dictionary<string, string>
        {
            ["Input.FinalAssessmentId"] = data.First.Id.ToString(),
            ["Input.ExpectedUpdatedAtUtc"] = Field(html, "Input.ExpectedUpdatedAtUtc"),
            ["Input.ExpectedFinalAssessmentId"] = Field(html, "Input.ExpectedFinalAssessmentId"),
            ["Input.OwnerTeacherUserId"] = Guid.NewGuid().ToString()
        };
        using var noToken = await client.PostAsync(path, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        fields["__RequestVerificationToken"] = Field(html, "__RequestVerificationToken");
        fields["Input.FinalAssessmentId"] = Guid.NewGuid().ToString();
        using var unavailable = await client.PostAsync(path, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, unavailable.StatusCode);
        Assert.Contains("Lựa chọn không còn hợp lệ", WebUtility.HtmlDecode(await unavailable.Content.ReadAsStringAsync()));
        Assert.Null((await CourseAsync(data)).FinalAssessmentId);
        fields["Input.FinalAssessmentId"] = data.First.Id.ToString();
        using var valid = await client.PostAsync(path, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        fields["Input.FinalAssessmentId"] = data.Second.Id.ToString();
        using var stale = await client.PostAsync(path, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.Contains("tải lại", WebUtility.HtmlDecode(await stale.Content.ReadAsStringAsync()), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(data.First.Id, (await CourseAsync(data)).FinalAssessmentId);
        using var manage = await client.GetAsync("/TeacherCourses/Manage/" + data.Course.Id);
        Assert.Contains("Chọn / quản lý bài kiểm tra", WebUtility.HtmlDecode(await manage.Content.ReadAsStringAsync()));

        // Kiểm tra controller lifecycle mới bằng HTTP; đây không phải test browser.
        using var list = await client.GetAsync("/TeacherAssessments");
        string listHtml = await list.Content.ReadAsStringAsync();
        Assert.Contains("TeacherAssessmentLifecycle/Unpublish", listHtml);
        var withdrawalFields = new Dictionary<string, string>
        {
            ["expectedUpdatedAtUtc"] = (await AssessmentAsync(data.First.Id)).UpdatedAtUtc!.Value.ToString("O")
        };
        using var noWithdrawalToken = await client.PostAsync("/TeacherAssessmentLifecycle/Unpublish/" + data.First.Id,
            new FormUrlEncodedContent(withdrawalFields));
        Assert.Equal(HttpStatusCode.BadRequest, noWithdrawalToken.StatusCode);
        withdrawalFields["__RequestVerificationToken"] = Field(listHtml, "__RequestVerificationToken");
        using var rejectedWithdrawal = await client.PostAsync("/TeacherAssessmentLifecycle/Unpublish/" + data.First.Id,
            new FormUrlEncodedContent(withdrawalFields));
        Assert.Equal(HttpStatusCode.Redirect, rejectedWithdrawal.StatusCode);
        using var message = await client.GetAsync(rejectedWithdrawal.Headers.Location);
        Assert.Contains("gỡ/thay Final", WebUtility.HtmlDecode(await message.Content.ReadAsStringAsync()));
        Assert.Equal(AssessmentStatus.Published, (await AssessmentAsync(data.First.Id)).Status);

        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var foreign = IdentityTestHelpers.CreateClient(factory);
        using var foreignLogin = await IdentityTestHelpers.LoginAsync(foreign, other);
        using var forbidden = await foreign.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        using var check = factory.Services.CreateScope();
        var service = check.ServiceProvider.GetRequiredService<ITeacherCourseFinalService>();
        Assert.Equal(LearningAccessResult.NotFound, (await service.SaveAsync(other.Id, data.Course.Id,
            Request(await CourseAsync(data), null))).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.GetEditorAsync(data.Student.Id, data.Course.Id)).Access);
        var lifecycle = check.ServiceProvider.GetRequiredService<ITeacherAssessmentLifecycleService>();
        Assert.Equal(LearningAccessResult.NotFound, (await lifecycle.ChangeStatusAsync(other.Id, data.First.Id,
            AssessmentStatus.Archived, (await AssessmentAsync(data.First.Id)).UpdatedAtUtc)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await lifecycle.ChangeStatusAsync(data.Student.Id, data.First.Id,
            AssessmentStatus.Unpublished, null)).Access);
    }

    // Các helper dùng scope riêng để không che lỗi do entity EF cũ được tracked.
    private async Task<Course> CourseAsync(Data data)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Courses.AsNoTracking().SingleAsync(c => c.Id == data.Course.Id);
    }
    private async Task<Assessment> AssessmentAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Assessments.AsNoTracking().SingleAsync(a => a.Id == id);
    }
    private async Task<TeacherCourseFinalEditor> EditorAsync(Data data)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ITeacherCourseFinalService>().GetEditorAsync(data.Teacher.Id, data.Course.Id);
        Assert.Equal(LearningAccessResult.Allowed, result.Access);
        return Assert.IsType<TeacherCourseFinalEditor>(result.Value);
    }
    private static CourseFinalRequest Request(Course course, Guid? id) => new()
    {
        FinalAssessmentId = id, ExpectedFinalAssessmentId = course.FinalAssessmentId, ExpectedUpdatedAtUtc = course.UpdatedAtUtc
    };
    private async Task<WriteOutcome> SaveAsync(Data data, Guid? id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITeacherCourseFinalService>()
            .SaveAsync(data.Teacher.Id, data.Course.Id, Request(await CourseAsync(data), id));
    }
    private async Task<DateTimeOffset?> CompletedAsync(Data data)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Enrollments.AsNoTracking()
            .Where(e => e.Id == data.Enrollment.Id).Select(e => e.CompletedAtUtc).SingleAsync();
    }
    private async Task<AttemptStartResult> StartAsync(Data data, Guid assessment)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAttemptService>().StartAsync(data.Student.Id, assessment);
    }
    private async Task<WriteOutcome> WithdrawAsync(Data data, AssessmentStatus target, DateTimeOffset? version)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITeacherAssessmentLifecycleService>()
            .ChangeStatusAsync(data.Teacher.Id, data.First.Id, target, version);
    }
    private static string Field(string html, string name)
    {
        var match = Regex.Match(html, "<input\\b[^>]*\\bname=\"" + Regex.Escape(name) + "\"[^>]*>");
        Assert.True(match.Success, "Missing form field " + name);
        // Razor có thể bỏ thuộc tính value khi hidden input có giá trị null.
        var value = Regex.Match(match.Value, "\\bvalue=\"([^\"]*)\"");
        return value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : string.Empty;
    }
    private async Task<Data> SeedAsync(AssessmentType type = AssessmentType.PracticeExam, bool completeLessons = false)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Final management " + Guid.NewGuid().ToString("N"), Status = CourseStatus.Published };
        var module = new Module { Course = course, Title = "Visible module" };
        var lesson = new Lesson { Module = module, Title = "Published lesson", Status = LessonStatus.Published,
            Resources = new List<LessonResource> { new() { ResourceType = LessonResourceType.Text, ContentText = "Lesson" } } };
        var enrollment = new Enrollment { Course = course, StudentUserId = student.Id };
        Assessment Build(string title, AssessmentType kind) => new()
        {
            Course = course, OwnerTeacherUserId = teacher.Id, Title = title, Status = AssessmentStatus.Published,
            AssessmentType = kind, TargetSkill = kind == AssessmentType.SkillAssessment ? EnglishSkill.Grammar : null,
            DurationMinutes = 20, MaxAttempts = 3, PassingScore = 70m, UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            Questions = Enumerable.Range(0, 5).Select(i => new AssessmentQuestion
            {
                OrderIndex = i, Points = 1m, Question = new Question
                {
                    OwnerTeacherUserId = teacher.Id, Content = title + " question " + i, PrimarySkill = EnglishSkill.Grammar,
                    Options = new List<QuestionOption> { new() { Content = "Correct", IsCorrect = true, OrderIndex = 0 }, new() { Content = "Wrong", OrderIndex = 1 } }
                }
            }).ToList()
        };
        var first = Build("First Final", type);
        var second = Build("Replacement Final", AssessmentType.SkillAssessment);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Lessons.Add(lesson); db.Enrollments.Add(enrollment); db.Assessments.AddRange(first, second);
        if (completeLessons) db.LessonProgressEntries.Add(new LessonProgress { Enrollment = enrollment, Lesson = lesson, IsCompleted = true });
        await db.SaveChangesAsync();
        return new Data(teacher, student, course, enrollment, first, second);
    }
    private sealed record Data(ApplicationUser Teacher, ApplicationUser Student, Course Course, Enrollment Enrollment, Assessment First, Assessment Second);

    // Một checkpoint SQL thật, không sleep: Start kết thúc trước khi Save lấy khóa Course.
    private sealed class BeforeCourseLock(Func<Task> competing) : DbCommandInterceptor
    {
        public bool Applied { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!Applied && command.CommandText.Contains("FROM Courses WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal))
            {
                Applied = true;
                await competing();
            }
            return result;
        }
    }
}
