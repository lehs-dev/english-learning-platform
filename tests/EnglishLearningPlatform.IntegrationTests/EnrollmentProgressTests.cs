using System.Net;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class EnrollmentProgressTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    private async Task<(Guid Student, Guid Teacher, Course Course)> Seed()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Progress course", Status = CourseStatus.Published,
            Modules = [new Module { Title = "Module", Lessons = [Lesson("One", 0), Lesson("Two", 1), new Lesson { Title = "Draft", OrderIndex = 2 }] },
                new Module { Title = "Hidden", Visibility = ModuleVisibility.Hidden, OrderIndex = 1, Lessons = [Lesson("Hidden lesson", 0)] }] };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        Assert.True((await scope.ServiceProvider.GetRequiredService<ICourseService>().EnrollFreeAsync(student.Id, course.Id)).Success);
        return (student.Id, teacher.Id, course);
    }

    private static Lesson Lesson(string title, int index) => new() { Title = title, OrderIndex = index, Status = LessonStatus.Published,
        Resources = [new LessonResource { ResourceType = LessonResourceType.Text, ContentText = title }] };

    [Fact]
    public async Task Progress_FollowsEffectiveLessonsAndPreservesFirstCompletion()
    {
        var data = await Seed();
        var module = data.Course.Modules.First();
        var first = module.Lessons.First(); var second = module.Lessons.ElementAt(1);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICourseService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await service.SetLessonCompletedAsync(data.Student, first.Id, true)).Success);
        var page = (await service.OpenCourseAsync(data.Student, data.Course.Id, false)).Value!;
        Assert.Equal(50m, page.Progress!.Percentage); Assert.Equal(2, page.Progress.TotalLessons);
        Assert.Null(page.Progress.CompletedAtUtc);
        Assert.True(page.Modules[0].Lessons[0].IsCompleted);
        Assert.Equal(50m, (await service.MyCoursesAsync(data.Student, false)).Value!.Single().Progress!.Percentage);
        Assert.True((await service.SaveContentAsync(data.Teacher, data.Course.Id, ContentKind.Lesson, module.Id, second.Id,
            new() { Title = second.Title, Status = LessonStatus.Hidden })).Success);
        page = (await service.OpenCourseAsync(data.Student, data.Course.Id, false)).Value!;
        Assert.Equal(100m, page.Progress!.Percentage);
        var completedAt = page.Progress.CompletedAtUtc;
        Assert.NotNull(completedAt);
        Assert.True((await service.SaveContentAsync(data.Teacher, data.Course.Id, ContentKind.Lesson, module.Id, second.Id,
            new() { Title = second.Title, Status = LessonStatus.Published })).Success);
        page = (await service.OpenCourseAsync(data.Student, data.Course.Id, false)).Value!;
        Assert.Equal(50m, page.Progress!.Percentage); Assert.Equal(completedAt, page.Progress.CompletedAtUtc);
        Assert.True((await service.SetLessonCompletedAsync(data.Student, first.Id, false)).Success);
        Assert.Equal(0m, (await service.OpenCourseAsync(data.Student, data.Course.Id, false)).Value!.Progress!.Percentage);
        Assert.True((await service.SaveContentAsync(data.Teacher, data.Course.Id, ContentKind.Module, data.Course.Id, module.Id,
            new() { Title = module.Title, Visibility = ModuleVisibility.Hidden })).Success);
        page = (await service.OpenCourseAsync(data.Student, data.Course.Id, false)).Value!;
        Assert.Null(page.Progress!.Percentage); Assert.Equal(0, page.Progress.TotalLessons);
        Assert.Null(page.Progress.ContinueLessonId); Assert.Equal(completedAt, page.Progress.CompletedAtUtc);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.SetLessonCompletedAsync(data.Student, first.Id, true)).Access);
        Assert.Single(await db.LessonProgressEntries.Where(p => p.Enrollment.StudentUserId == data.Student).ToListAsync());
    }

    [Fact]
    public async Task Mutations_AreIdempotentConcurrentAndEnforceLifecycleAndOwnership()
    {
        var data = await Seed(); var lesson = data.Course.Modules.First().Lessons.First();
        var newStudent = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            using var scope = factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ICourseService>();
            Assert.True((await service.EnrollFreeAsync(newStudent.Id, data.Course.Id)).Success);
            Assert.True((await service.SetLessonCompletedAsync(data.Student, lesson.Id, true)).Success);
        }));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var service = scope.ServiceProvider.GetRequiredService<ICourseService>();
        Assert.Equal(1, await db.Enrollments.CountAsync(e => e.StudentUserId == data.Student && e.CourseId == data.Course.Id));
        Assert.Equal(1, await db.Enrollments.CountAsync(e => e.StudentUserId == newStudent.Id && e.CourseId == data.Course.Id));
        Assert.Equal(1, await db.LessonProgressEntries.CountAsync(p => p.Enrollment.StudentUserId == data.Student && p.LessonId == lesson.Id));
        Assert.True((await service.OpenLessonAsync(data.Student, lesson.Id)).Value!.CanRecordProgress);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.SetLessonCompletedAsync(data.Teacher, lesson.Id, true)).Access);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.SetLessonCompletedAsync(other.Id, lesson.Id, true)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.SetLessonCompletedAsync(data.Student, data.Course.Modules.First().Lessons.Last().Id, true)).Access);
        Assert.True((await service.ChangeStatusAsync(data.Teacher, data.Course.Id, CourseStatus.Unpublished)).Success);
        Assert.True((await service.SetLessonCompletedAsync(data.Student, lesson.Id, false)).Success);
        Assert.Equal(LearningAccessResult.NotFound, (await service.EnrollFreeAsync(other.Id, data.Course.Id)).Access);
        Assert.True((await service.ChangeStatusAsync(data.Teacher, data.Course.Id, CourseStatus.Archived)).Success);
        Assert.False((await service.OpenLessonAsync(data.Student, lesson.Id)).Value!.CanRecordProgress);
        Assert.Equal(LearningAccessResult.Allowed, (await service.OpenModuleAsync(data.Student, data.Course.Modules.First().Id)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.OpenModuleAsync(data.Student, data.Course.Modules.Last().Id)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.SetLessonCompletedAsync(data.Student, lesson.Id, true)).Access);
        Assert.Single((await service.MyCoursesAsync(data.Student, false)).Value!);
    }

    [Fact]
    public async Task Completion_PostRequiresCsrfAndExplicitState_ModuleAndLessonRenderProgress()
    {
        var data = await Seed(); var lesson = data.Course.Modules.First().Lessons.First();
        using var scope = factory.Services.CreateScope();
        var student = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Id == data.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        using var missing = await client.PostAsync($"/Learning/Completion/{lesson.Id}", new FormUrlEncodedContent(new Dictionary<string, string> { ["completed"] = "true" }));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Learning/Lesson/{lesson.Id}");
        using var posted = await client.PostAsync($"/Learning/Completion/{lesson.Id}", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["completed"] = "true", ["__RequestVerificationToken"] = token, ["EnrollmentId"] = Guid.NewGuid().ToString() }));
        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        using var module = await client.GetAsync($"/Learning/Module/{data.Course.Modules.First().Id}");
        Assert.Equal(HttpStatusCode.OK, module.StatusCode); Assert.Contains("50%", await module.Content.ReadAsStringAsync());
        using var invalid = await client.PostAsync($"/Learning/Completion/{lesson.Id}", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task EmptyEffectiveSet_DoesNotCompleteEnrollment()
    {
        var data = await Seed();
        using var scope = factory.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<ICourseService>();
        var module = data.Course.Modules.First();
        Assert.True((await service.SaveContentAsync(data.Teacher, data.Course.Id, ContentKind.Module, data.Course.Id, module.Id,
            new() { Title = module.Title, Visibility = ModuleVisibility.Hidden })).Success);
        var progress = (await service.MyCoursesAsync(data.Student, false)).Value!.Single().Progress!;
        Assert.Null(progress.Percentage); Assert.Null(progress.CompletedAtUtc); Assert.Null(progress.ContinueLessonId);
    }

    [Fact]
    public async Task FinalAssessment_RequiresGradedPassingScoreBeforeHistoricalCompletion()
    {
        var data = await Seed();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ICourseService>();
        var final = new Assessment { OwnerTeacherUserId = data.Teacher, CourseId = data.Course.Id, Title = "Final",
            AssessmentType = AssessmentType.PracticeExam, Status = AssessmentStatus.Published, PassingScore = 70, DurationMinutes = 15, MaxAttempts = 3 };
        db.Assessments.Add(final); await db.SaveChangesAsync();
        await db.Courses.Where(c => c.Id == data.Course.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.FinalAssessmentId, final.Id));
        foreach (var lesson in data.Course.Modules.First().Lessons.Where(l => l.Status == LessonStatus.Published))
            Assert.True((await service.SetLessonCompletedAsync(data.Student, lesson.Id, true)).Success);
        Assert.Equal(100m, (await service.OpenCourseAsync(data.Student, data.Course.Id, false)).Value!.Progress!.Percentage);
        Assert.Null((await service.OpenCourseAsync(data.Student, data.Course.Id, false)).Value!.Progress!.CompletedAtUtc);
        var attempt = new Attempt { StudentUserId = data.Student, AssessmentId = final.Id, Status = AttemptStatus.Graded,
            OverallScore = 69.99m, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(15), FinalizedAtUtc = DateTimeOffset.UtcNow, FinalizationReason = AttemptFinalizationReason.ManualSubmit };
        db.Attempts.Add(attempt); await db.SaveChangesAsync();
        var first = data.Course.Modules.First().Lessons.First();
        Assert.True((await service.SetLessonCompletedAsync(data.Student, first.Id, true)).Success);
        Assert.Null((await service.OpenCourseAsync(data.Student, data.Course.Id, false)).Value!.Progress!.CompletedAtUtc);
        attempt.OverallScore = 70; await db.SaveChangesAsync();
        Assert.True((await service.SetLessonCompletedAsync(data.Student, first.Id, true)).Success);
        Assert.NotNull((await service.OpenCourseAsync(data.Student, data.Course.Id, false)).Value!.Progress!.CompletedAtUtc);
    }
}
