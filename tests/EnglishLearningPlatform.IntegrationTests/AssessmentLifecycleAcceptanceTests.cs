using System.Data.Common;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.AssessmentAttempts;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class AssessmentLifecycleAcceptanceTests(IntegrationTestFactory factory)
    : IClassFixture<IntegrationTestFactory>
{
    private async Task<(Guid Student, Guid Assessment, Guid? Course)> SeedAsync(bool courseLinked = false)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Course? course = courseLinked
            ? new Course { OwnerTeacherUserId = teacher.Id, Title = "Attempt course", Status = CourseStatus.Published }
            : null;
        if (course is not null)
        {
            db.Courses.Add(course);
            db.Enrollments.Add(new Enrollment { StudentUserId = student.Id, Course = course });
        }

        var assessment = new Assessment
        {
            OwnerTeacherUserId = teacher.Id,
            Course = course,
            Title = "Lifecycle acceptance",
            AssessmentType = AssessmentType.SkillAssessment,
            TargetSkill = EnglishSkill.Grammar,
            Status = AssessmentStatus.Published,
            DurationMinutes = 20,
            MaxAttempts = 2,
            PassingScore = 70m,
            Questions = Enumerable.Range(0, 5).Select(index => new AssessmentQuestion
            {
                OrderIndex = index,
                Points = 1m,
                Question = new Question
                {
                    OwnerTeacherUserId = teacher.Id,
                    Content = $"Question {index}",
                    PrimarySkill = EnglishSkill.Grammar,
                    Status = QuestionStatus.Published,
                    Options =
                    [
                        new QuestionOption { OrderIndex = 0, Content = "Correct", IsCorrect = true },
                        new QuestionOption { OrderIndex = 1, Content = "Incorrect" }
                    ]
                }
            }).ToList()
        };
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();
        return (student.Id, assessment.Id, course?.Id);
    }

    private async Task<AttemptStartResult> StartAsync(Guid student, Guid assessment)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAttemptService>().StartAsync(student, assessment);
    }

    private async Task<AttemptFinalizeResult> FinalizeAsync(Guid student, Guid attempt)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAttemptService>().FinalizeAsync(student, attempt);
    }

    [Theory]
    [InlineData(AssessmentStatus.Unpublished, false)]
    [InlineData(AssessmentStatus.Archived, false)]
    [InlineData(AssessmentStatus.Unpublished, true)]
    [InlineData(AssessmentStatus.Archived, true)]
    public async Task ExistingAttempt_ResumesOrFinalizesAfterAssessmentWithdrawn(AssessmentStatus status, bool expired)
    {
        var data = await SeedAsync();
        var started = await StartAsync(data.Student, data.Assessment);
        Assert.Equal(AttemptStartStatus.Started, started.Status);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deadline = expired ? DateTimeOffset.UtcNow.AddMinutes(-1) : started.DeadlineUtc!.Value;
        await db.Assessments.Where(a => a.Id == data.Assessment)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status));
        await db.Attempts.Where(a => a.Id == started.AttemptId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.DeadlineUtc, deadline));

        var resumed = await StartAsync(data.Student, data.Assessment);

        Assert.Equal(expired ? AttemptStartStatus.ExpiredFinalized : AttemptStartStatus.Resumed, resumed.Status);
        Assert.Equal(started.AttemptId, resumed.AttemptId);
        Assert.Equal(deadline, resumed.DeadlineUtc);
        var saved = await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == started.AttemptId);
        Assert.Equal(expired ? AttemptStatus.Graded : AttemptStatus.InProgress, saved.Status);
        Assert.Equal(deadline, saved.DeadlineUtc);
        Assert.Equal(1, await db.Attempts.CountAsync(a => a.StudentUserId == data.Student && a.AssessmentId == data.Assessment));
        if (expired)
        {
            Assert.Equal(AttemptFinalizationReason.DeadlineElapsed, saved.FinalizationReason);
            Assert.Equal(0m, saved.OverallScore);
            Assert.False(saved.Passed);
        }
    }

    [Theory]
    [InlineData(CourseStatus.Unpublished, false)]
    [InlineData(CourseStatus.Archived, false)]
    [InlineData(CourseStatus.Unpublished, true)]
    [InlineData(CourseStatus.Archived, true)]
    public async Task ExistingCourseAttempt_PreservesDeadlineAndCanFinishAfterArchive(CourseStatus status, bool expired)
    {
        var data = await SeedAsync(courseLinked: true);
        var started = await StartAsync(data.Student, data.Assessment);
        Assert.Equal(AttemptStartStatus.Started, started.Status);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deadline = expired ? DateTimeOffset.UtcNow.AddMinutes(-1) : started.DeadlineUtc!.Value;
        await db.Courses.Where(c => c.Id == data.Course).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, status));
        await db.Attempts.Where(a => a.Id == started.AttemptId).ExecuteUpdateAsync(s => s.SetProperty(a => a.DeadlineUtc, deadline));

        var resumed = await StartAsync(data.Student, data.Assessment);

        Assert.Equal(expired ? AttemptStartStatus.ExpiredFinalized : AttemptStartStatus.Resumed, resumed.Status);
        Assert.Equal(started.AttemptId, resumed.AttemptId);
        Assert.Equal(deadline, resumed.DeadlineUtc);
        if (!expired)
            Assert.Equal(AttemptFinalizeStatus.Graded, (await FinalizeAsync(data.Student, started.AttemptId!.Value)).Status);
        Assert.Equal(AttemptStatus.Graded, (await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == started.AttemptId)).Status);
        Assert.Equal(1, await db.Attempts.CountAsync(a => a.StudentUserId == data.Student && a.AssessmentId == data.Assessment));
        if (status == CourseStatus.Archived)
            Assert.Equal(AttemptStartStatus.Unavailable, (await StartAsync(data.Student, data.Assessment)).Status);
    }

    [Theory]
    [InlineData(AssessmentStatus.Draft)]
    [InlineData(AssessmentStatus.Unpublished)]
    [InlineData(AssessmentStatus.Archived)]
    public async Task UnavailableAssessment_DoesNotStartNewAttempt(AssessmentStatus status)
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Assessments.Where(a => a.Id == data.Assessment).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status));

        Assert.Equal(AttemptStartStatus.Unavailable, (await StartAsync(data.Student, data.Assessment)).Status);
        Assert.False(await db.Attempts.AnyAsync(a => a.StudentUserId == data.Student && a.AssessmentId == data.Assessment));
    }

    [Theory]
    [InlineData(CourseStatus.Draft)]
    [InlineData(CourseStatus.Archived)]
    public async Task UnavailableCourse_DoesNotStartNewAttempt(CourseStatus status)
    {
        var data = await SeedAsync(courseLinked: true);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Courses.Where(c => c.Id == data.Course).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, status));

        Assert.Equal(AttemptStartStatus.Unavailable, (await StartAsync(data.Student, data.Assessment)).Status);
        Assert.False(await db.Attempts.AnyAsync(a => a.StudentUserId == data.Student && a.AssessmentId == data.Assessment));
    }

    [Theory]
    [InlineData(AccountStatus.Locked)]
    [InlineData(AccountStatus.Disabled)]
    public async Task Finalize_BlocksInactiveStudentWithoutMutatingAttempt(AccountStatus status)
    {
        var data = await SeedAsync();
        var started = await StartAsync(data.Student, data.Assessment);
        Assert.Equal(AttemptStartStatus.Started, started.Status);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users.Where(u => u.Id == data.Student).ExecuteUpdateAsync(s => s.SetProperty(u => u.AccountStatus, status));

        Assert.Equal(AttemptFinalizeStatus.Forbidden, (await FinalizeAsync(data.Student, started.AttemptId!.Value)).Status);
        var saved = await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == started.AttemptId);
        Assert.Equal(AttemptStatus.InProgress, saved.Status);
        Assert.Null(saved.OverallScore);
        Assert.Null(saved.Passed);
        Assert.Null(saved.FinalizedAtUtc);
    }

    [Theory]
    [InlineData(AppRoles.Teacher)]
    [InlineData(AppRoles.Admin)]
    [InlineData(null)]
    public async Task Finalize_RequiresExactlyStudentRole(string? replacementRole)
    {
        var data = await SeedAsync();
        var started = await StartAsync(data.Student, data.Assessment);
        Assert.Equal(AttemptStartStatus.Started, started.Status);
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var student = await manager.FindByIdAsync(data.Student.ToString());
        Assert.NotNull(student);
        Assert.True((await manager.RemoveFromRoleAsync(student, AppRoles.Student)).Succeeded);
        if (replacementRole is not null)
            Assert.True((await manager.AddToRoleAsync(student, replacementRole)).Succeeded);

        Assert.Equal(AttemptFinalizeStatus.Forbidden, (await FinalizeAsync(data.Student, started.AttemptId!.Value)).Status);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == started.AttemptId);
        Assert.Equal(AttemptStatus.InProgress, saved.Status);
        Assert.Null(saved.OverallScore);
        Assert.Null(saved.FinalizedAtUtc);
    }

    [Fact]
    public async Task Finalize_ConcealsAnotherStudentsAttemptAndPreservesResult()
    {
        var data = await SeedAsync();
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var started = await StartAsync(data.Student, data.Assessment);
        Assert.Equal(AttemptStartStatus.Started, started.Status);

        Assert.Equal(AttemptFinalizeStatus.NotFound, (await FinalizeAsync(other.Id, started.AttemptId!.Value)).Status);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == started.AttemptId);
        Assert.Equal(AttemptStatus.InProgress, saved.Status);
        Assert.Null(saved.OverallScore);
    }

    [Fact]
    public async Task SaveAnswer_RejectsAnotherAssessmentsQuestion_AndClearIsIdempotent()
    {
        var data = await SeedAsync();
        var foreign = await SeedAsync();
        var started = await StartAsync(data.Student, data.Assessment);
        Assert.Equal(AttemptStartStatus.Started, started.Status);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IAttemptService>();
        var questions = await db.AssessmentQuestions.AsNoTracking()
            .Where(q => q.AssessmentId == data.Assessment || q.AssessmentId == foreign.Assessment)
            .OrderBy(q => q.OrderIndex)
            .Select(q => new { q.Id, q.AssessmentId, CorrectOption = q.Question.Options.Where(o => o.IsCorrect).Select(o => o.Id).Single() })
            .ToListAsync();
        var ownQuestion = questions.First(q => q.AssessmentId == data.Assessment);
        var foreignQuestion = questions.First(q => q.AssessmentId == foreign.Assessment);
        var attempt = started.AttemptId!.Value;

        Assert.Equal(AttemptSaveStatus.InvalidAnswer,
            await service.SaveAnswerAsync(data.Student, attempt, foreignQuestion.Id, foreignQuestion.CorrectOption));
        Assert.False(await db.AttemptAnswers.AnyAsync(a => a.AttemptId == attempt));
        Assert.Equal(AttemptSaveStatus.Saved,
            await service.SaveAnswerAsync(data.Student, attempt, ownQuestion.Id, ownQuestion.CorrectOption));
        Assert.Equal(1, await db.AttemptAnswers.CountAsync(a => a.AttemptId == attempt));
        Assert.Equal(AttemptSaveStatus.Cleared, await service.SaveAnswerAsync(data.Student, attempt, ownQuestion.Id, null));
        Assert.Equal(AttemptSaveStatus.Cleared, await service.SaveAnswerAsync(data.Student, attempt, ownQuestion.Id, null));
        Assert.False(await db.AttemptAnswers.AnyAsync(a => a.AttemptId == attempt));
        var saved = await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == attempt);
        Assert.Equal(AttemptStatus.InProgress, saved.Status);
        Assert.Equal(started.DeadlineUtc, saved.DeadlineUtc);
    }

    [Fact]
    public async Task Start_RechecksAssessmentScopeAfterSnapshotBeforeCreatingAttempt()
    {
        var data = await SeedAsync(courseLinked: true);
        using var scope = factory.Services.CreateScope();
        var seedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var original = await seedDb.Courses.AsNoTracking().SingleAsync(c => c.Id == data.Course);
        var otherCourse = new Course { OwnerTeacherUserId = original.OwnerTeacherUserId, Title = "Other cohort", Status = CourseStatus.Published };
        seedDb.Courses.Add(otherCourse);
        await seedDb.SaveChangesAsync();
        var connectionString = seedDb.Database.GetConnectionString()!;
        var interceptor = new ScopeChangeBeforeCourseLock(connectionString, data.Assessment, otherCourse.Id);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).AddInterceptors(interceptor).Options;
        await using var db = new AppDbContext(options);
        var service = new AssessmentAttemptService(db, scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            scope.ServiceProvider.GetRequiredService<IExamScoringService>());

        var result = await service.StartAsync(data.Student, data.Assessment);

        Assert.True(interceptor.ScopeChanged);
        Assert.Equal(AttemptStartStatus.Unavailable, result.Status);
        Assert.False(await seedDb.Attempts.AnyAsync(a => a.StudentUserId == data.Student && a.AssessmentId == data.Assessment));
        Assert.Equal(otherCourse.Id, await seedDb.Assessments.Where(a => a.Id == data.Assessment).Select(a => a.CourseId).SingleAsync());
    }

    private sealed class ScopeChangeBeforeCourseLock(string connectionString, Guid assessmentId, Guid otherCourseId)
        : DbCommandInterceptor
    {
        public bool ScopeChanged { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!ScopeChanged && command.CommandText.Contains("FROM Courses WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal))
            {
                ScopeChanged = true;
                var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
                await using var mutation = new AppDbContext(options);
                await mutation.Assessments.Where(a => a.Id == assessmentId)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.CourseId, (Guid?)otherCourseId), cancellationToken);
            }
            return result;
        }
    }
}
