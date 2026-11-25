using System.Net;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.LearningPath;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.LearningPath;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class LearningPathAuthorizationTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task NewStudent_NeedsAssessmentAndCannotCreateEmptyPlan()
    {
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ILearningPathService>();
        var page = await service.GetActivePlanAsync(student.Id);
        Assert.Equal(LearningAccessResult.Allowed, page.Access);
        Assert.Equal(LearningPathReadiness.NeedsAssessment, page.Value!.Readiness);
        Assert.Null((await service.GeneratePlanAsync(student.Id)).Value!.Plan);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.GeneratePlanAsync(teacher.Id)).Access);
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<LearningPlan>().AnyAsync(p => p.StudentUserId == student.Id));
    }

    [Fact]
    public async Task Generate_UsesOnlyEnrolledPublishedContentAndIsConcurrentIdempotent()
    {
        var data = await SeedAsync();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ILearningPathService>().GeneratePlanAsync(data.Student);
        }));
        Assert.All(outcomes, result => Assert.Equal(LearningAccessResult.Allowed, result.Access));
        var plan = outcomes[0].Value!.Plan!;
        Assert.Equal(plan.Id, outcomes[1].Value!.Plan!.Id);
        Assert.Equal(14, plan.Items.Count);
        Assert.Equal(Enumerable.Range(1, 7), plan.Items.Select(i => i.DayIndex).Distinct());
        Assert.All(plan.Items, item =>
        {
            Assert.Contains(item.TargetId, data.AllowedTargets);
            Assert.Equal(EnglishSkill.Grammar, item.Skill);
        });
        using var verify = factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Set<LearningPlan>().CountAsync(p => p.StudentUserId == data.Student && p.Status == LearningPlanStatus.Active));
        Assert.Equal(data.Attempt, plan.SourceAssessmentAttemptId);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(30)]
    public async Task Generate_UsesRequestedDurationAndPreservesHistoryWhenCycleExpires(int durationDays)
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new AdjustableClock(DateTimeOffset.UtcNow);
        var service = new LearningPathService(db, clock);

        var initial = (await service.GetActivePlanAsync(data.Student)).Value!;
        Assert.Equal([7, 14, 30], initial.AvailableDurations);
        var plan = (await service.GeneratePlanAsync(data.Student, durationDays)).Value!.Plan!;
        Assert.Equal(durationDays, plan.DurationDays);
        Assert.Equal(plan.StartDate.AddDays(durationDays - 1), plan.EndDate);
        Assert.Equal(Enumerable.Range(1, durationDays), plan.Items.Select(i => i.DayIndex).Distinct());
        Assert.Equal(LearningAccessResult.Allowed, await service.SkipItemAsync(data.Student, plan.Items[0].Id));

        clock.AdvanceDays(7);
        var stillActive = (await service.GeneratePlanAsync(data.Student, 7)).Value!;
        Assert.Equal(plan.Id, stillActive.Plan!.Id);
        Assert.Equal(durationDays, stillActive.Plan.DurationDays);
        Assert.False(stillActive.CanGenerate);

        clock.AdvanceDays(durationDays - 7);
        var expired = (await service.GetActivePlanAsync(data.Student)).Value!;
        Assert.True(expired.CanGenerate);
        Assert.Equal(LearningAccessResult.Forbidden, await service.SkipItemAsync(data.Student, plan.Items[1].Id));
        var replacement = (await service.GeneratePlanAsync(data.Student)).Value!.Plan!;
        Assert.NotEqual(plan.Id, replacement.Id);
        Assert.Equal(7, replacement.DurationDays);
        var archived = await db.Set<LearningPlan>().AsNoTracking().Include(p => p.Items).SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(LearningPlanStatus.Archived, archived.Status);
        Assert.Equal(durationDays, archived.DurationDays);
        Assert.Equal(LearningPlanItemStatus.Skipped, archived.Items.Single(i => i.Id == plan.Items[0].Id).Status);
    }

    [Fact]
    public async Task GenerateForm_RejectsUnsupportedDurationAndOpensFinalDayOfThirtyDayPlan()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var student = await db.Users.AsNoTracking().SingleAsync(u => u.Id == data.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var token = await IdentityTestHelpers.GetTokenAsync(client, "/LearningPath");

        using var invalid = await client.PostAsync("/LearningPath/Generate", DurationForm("31", token));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.False(await db.Set<LearningPlan>().AnyAsync(p => p.StudentUserId == data.Student));

        using var valid = await client.PostAsync("/LearningPath/Generate", DurationForm("30", token));
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(valid.Headers.Location!));
        Assert.Contains("Lộ trình học 30 ngày", html);
        Assert.Contains("id=\"path-day-30\"", html);
    }

    [Fact]
    public async Task Skip_EnforcesStudentOwnershipAndPreservesCompletedEvidence()
    {
        var data = await SeedAsync();
        var outsider = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ILearningPathService>();
        var plan = (await service.GeneratePlanAsync(data.Student)).Value!.Plan!;
        var item = plan.Items[0];
        Assert.Equal(LearningAccessResult.NotFound, await service.SkipItemAsync(outsider.Id, item.Id));
        Assert.Equal(LearningAccessResult.Forbidden, await service.SkipItemAsync(data.Teacher, item.Id));
        Assert.Equal(LearningAccessResult.Allowed, await service.SkipItemAsync(data.Student, item.Id));
        Assert.Equal(LearningAccessResult.Allowed, await service.SkipItemAsync(data.Student, item.Id));
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<LearningPlanItem>().SingleAsync(i => i.Id == item.Id);
        Assert.Equal(LearningPlanItemStatus.Skipped, stored.Status);
        Assert.Null(stored.CompletedAtUtc);
    }

    [Fact]
    public async Task Completion_RequiresSavedEvidenceAndWithdrawalProjectsUnavailableWithoutChangingHistory()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ILearningPathService>();
        var plan = (await service.GeneratePlanAsync(data.Student)).Value!.Plan!;
        var lesson = plan.Items.Single(i => i.TargetType == LearningPlanTargetType.Lesson);
        await service.TrackCompletionAsync(data.Student, LearningPlanTargetType.Lesson, lesson.TargetId);
        Assert.Equal(LearningPlanItemStatus.Pending, (await service.GetActivePlanAsync(data.Student)).Value!.Plan!.Items.Single(i => i.Id == lesson.Id).Status);
        var completedAt = DateTimeOffset.UtcNow;
        db.LessonProgressEntries.Add(new LessonProgress
        {
            EnrollmentId = data.Enrollment,
            LessonId = lesson.TargetId,
            IsCompleted = true,
            UpdatedAtUtc = completedAt
        });
        await db.SaveChangesAsync();
        await service.TrackCompletionAsync(data.Student, LearningPlanTargetType.Lesson, lesson.TargetId);
        await service.TrackCompletionAsync(data.Student, LearningPlanTargetType.Lesson, lesson.TargetId);
        var completed = (await service.GetActivePlanAsync(data.Student)).Value!.Plan!.Items.Single(i => i.Id == lesson.Id);
        Assert.Equal(LearningPlanItemStatus.Completed, completed.Status);
        Assert.Equal(completedAt, completed.CompletedAtUtc);
        Assert.All((await service.GetActivePlanAsync(data.Student)).Value!.Plan!.Items.Where(i => i.DayIndex > 1),
            i => Assert.Equal(LearningPlanItemStatus.Pending, i.Status));

        await db.Courses.Where(c => c.Id == data.Course).ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, CourseStatus.Unpublished));
        var unavailable = (await service.GetActivePlanAsync(data.Student)).Value!.Plan!;
        Assert.Equal(LearningPlanItemStatus.Completed, unavailable.Items.Single(i => i.Id == lesson.Id).Status);
        Assert.All(unavailable.Items.Where(i => i.Id != lesson.Id), i => Assert.Equal(LearningPlanItemStatus.Unavailable, i.Status));
        // Availability is a read projection. It must not overwrite the task history or the source assessment result.
        Assert.All(await db.Set<LearningPlanItem>().AsNoTracking().Where(i => i.PlanId == plan.Id && i.Id != lesson.Id).ToListAsync(),
            i => Assert.Equal(LearningPlanItemStatus.Pending, i.Status));
        Assert.Equal(0m, await db.Attempts.Where(a => a.Id == data.Attempt).Select(a => a.OverallScore).SingleAsync());
        await db.Courses.Where(c => c.Id == data.Course).ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, CourseStatus.Published));
        Assert.All((await service.GetActivePlanAsync(data.Student)).Value!.Plan!.Items.Where(i => i.Id != lesson.Id),
            i => Assert.Equal(LearningPlanItemStatus.Pending, i.Status));
    }

    private async Task<SeedData> SeedAsync()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new Course
        {
            OwnerTeacherUserId = teacher.Id, Title = "Path course " + Guid.NewGuid(), Status = CourseStatus.Published,
            Skills = [new CourseSkill { Skill = EnglishSkill.Grammar }]
        };
        var module = new Module { Course = course, Title = "Available module" };
        var lesson = new Lesson { Module = module, Title = "Available lesson", Status = LessonStatus.Published };
        var first = Practice(teacher.Id, module, "First published practice", PracticeStatus.Published);
        var second = Practice(teacher.Id, module, "Second published practice", PracticeStatus.Published);
        var hiddenModule = new Module { Course = course, Title = "Hidden module", Visibility = ModuleVisibility.Hidden, OrderIndex = 1 };
        var hidden = Practice(teacher.Id, hiddenModule, "Hidden practice", PracticeStatus.Published);
        var draft = Practice(teacher.Id, module, "Draft practice", PracticeStatus.Draft);
        var foreignCourse = new Course
        {
            OwnerTeacherUserId = teacher.Id, Title = "Not enrolled", Status = CourseStatus.Published,
            Skills = [new CourseSkill { Skill = EnglishSkill.Grammar }]
        };
        var foreign = Practice(teacher.Id, new Module { Course = foreignCourse, Title = "Other module" }, "Not enrolled practice", PracticeStatus.Published);
        var enrollment = new Enrollment { StudentUserId = student.Id, Course = course };
        var assessment = new Assessment
        {
            OwnerTeacherUserId = teacher.Id, Course = course, Title = "Grammar assessment", Status = AssessmentStatus.Published,
            AssessmentType = AssessmentType.PracticeExam, DurationMinutes = 15, MaxAttempts = 10,
            Questions = Enumerable.Range(0, 5).Select(index => new AssessmentQuestion
            {
                OrderIndex = index, Points = 1, Question = Question(teacher.Id, index < 3 ? EnglishSkill.Grammar : EnglishSkill.Reading)
            }).ToList()
        };
        db.Practices.AddRange(first, second, hidden, draft, foreign);
        db.Lessons.Add(lesson);
        db.Enrollments.Add(enrollment);
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();
        var attempt = new Attempt
        {
            StudentUserId = student.Id, AssessmentId = assessment.Id, Status = AttemptStatus.Graded,
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(-5), StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
            FinalizedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1), FinalizationReason = AttemptFinalizationReason.ManualSubmit,
            OverallScore = 0, Passed = false,
            SkillResults = [new AttemptSkillResult { Skill = EnglishSkill.Grammar, Score = 0, QuestionCount = 3 }]
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        return new(student.Id, teacher.Id, course.Id, enrollment.Id, attempt.Id, [lesson.Id, first.Id, second.Id]);
    }

    private static Practice Practice(Guid teacher, Module module, string title, PracticeStatus status) => new()
    {
        OwnerTeacherUserId = teacher, Module = module, Title = title, Status = status,
        Questions = [new PracticeQuestion { Question = Question(teacher, EnglishSkill.Grammar), OrderIndex = 0 }]
    };

    private static Question Question(Guid teacher, EnglishSkill skill) => new()
    {
        OwnerTeacherUserId = teacher, Content = "Choose an answer", PrimarySkill = skill, Status = QuestionStatus.Published,
        Options = [new QuestionOption { Content = "Correct", IsCorrect = true }, new QuestionOption { Content = "Wrong", OrderIndex = 1 }]
    };

    private static FormUrlEncodedContent DurationForm(string days, string token) => new(new Dictionary<string, string>
    {
        ["durationDays"] = days,
        ["__RequestVerificationToken"] = token
    });

    private sealed record SeedData(Guid Student, Guid Teacher, Guid Course, Guid Enrollment, Guid Attempt, Guid[] AllowedTargets);

    private sealed class AdjustableClock(DateTimeOffset startingAt) : TimeProvider
    {
        private DateTimeOffset now = startingAt;
        public override DateTimeOffset GetUtcNow() => now;
        public void AdvanceDays(int days) => now = now.AddDays(days);
    }
}
