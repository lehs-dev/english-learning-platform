using System.Net;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.TeacherAnalytics;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using EnglishLearningPlatform.Infrastructure.TeacherAnalytics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class TeacherAnalyticsAuthorizationTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task Aggregations_AreOwnerScoped_AndEmptyDataHasNoDivisionByZero()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var own = new Course { OwnerTeacherUserId = teacher.Id, Title = "My analytics course", Status = CourseStatus.Published };
        var foreign = new Course { OwnerTeacherUserId = other.Id, Title = "Private other course", Status = CourseStatus.Published };
        var module = new Module { Course = own, Title = "Module", Visibility = ModuleVisibility.Visible };
        var lesson = new Lesson { Module = module, Title = "Lesson", Status = LessonStatus.Published };
        var enrollment = new Enrollment { Course = own, StudentUserId = student.Id, CompletedAtUtc = DateTimeOffset.UtcNow };
        db.AddRange(lesson, foreign, enrollment);
        db.LessonProgressEntries.Add(new() { Enrollment = enrollment, Lesson = lesson, IsCompleted = true });
        var practice = new Practice { Lesson = lesson, OwnerTeacherUserId = teacher.Id, Title = "Practice", Status = PracticeStatus.Published };
        db.PracticeSubmissions.Add(new() { Practice = practice, StudentUserId = student.Id, Score = 80 });
        await db.SaveChangesAsync();
        var analytics = scope.ServiceProvider.GetRequiredService<ITeacherAnalyticsQueryService>();
        var ownResult = await analytics.GetAsync(teacher.Id, new());
        Assert.Equal(LearningAccessResult.Allowed, ownResult.Access);
        Assert.Equal(1, ownResult.Value!.StudentCount);
        Assert.Equal(100m, ownResult.Value.CompletionPercent);
        Assert.Equal(80m, ownResult.Value.AveragePracticeScore);
        Assert.Null(ownResult.Value.AverageAssessmentScore);
        Assert.Single(ownResult.Value.Students);
        Assert.DoesNotContain(ownResult.Value.Courses, c => c.Id == foreign.Id);
        Assert.Equal(LearningAccessResult.Forbidden, (await analytics.GetAsync(teacher.Id, new() { CourseId = foreign.Id })).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await analytics.GetAsync(student.Id, new())).Access);
        var empty = await analytics.GetAsync(other.Id, new());
        Assert.Equal(0, empty.Value!.StudentCount);
        Assert.Equal(0m, empty.Value.CompletionPercent);
        Assert.Empty(empty.Value.Students);
    }

    [Fact]
    public async Task DateRange_UsesVietnamMidnights_AndFillBlankPersistedGradeIsAggregatedInDatabase()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var start = DateTimeOffset.Parse("2026-11-18T17:00:00Z");
        var own = new Course { OwnerTeacherUserId = teacher.Id, Title = "Own boundary course", Status = CourseStatus.Published };
        var foreign = new Course { OwnerTeacherUserId = other.Id, Title = "Foreign boundary course", Status = CourseStatus.Published };
        var module = new Module { Course = own, Title = "Own module", Visibility = ModuleVisibility.Visible };
        var foreignModule = new Module { Course = foreign, Title = "Foreign module", Visibility = ModuleVisibility.Visible };
        var fill = new Question { OwnerTeacherUserId = teacher.Id, Content = "Fill ___", QuestionType = QuestionType.FillBlank,
            PrimarySkill = EnglishSkill.Grammar, AcceptedAnswers = [new() { AnswerText = "lived" }] };
        var correct = new QuestionOption { Content = "Yes", IsCorrect = true };
        var mcq = new Question { OwnerTeacherUserId = teacher.Id, Content = "Choose", PrimarySkill = EnglishSkill.Grammar,
            Options = [correct, new() { Content = "No", OrderIndex = 1 }] };
        var fillItem = new PracticeQuestion { Question = fill };
        var mcqItem = new PracticeQuestion { Question = mcq, OrderIndex = 1 };
        Practice Make(Module parent, Guid owner, string title) => new() { Module = parent, OwnerTeacherUserId = owner, Title = title, Status = PracticeStatus.Published };
        var inside = Make(module, teacher.Id, "Included at start");
        inside.Questions = [fillItem, mcqItem];
        var excluded = Make(module, teacher.Id, "Excluded at next midnight");
        var foreignPractice = Make(foreignModule, other.Id, "Other owner's private practice");
        db.Enrollments.AddRange(new Enrollment { Course = own, StudentUserId = student.Id, EnrolledAtUtc = start.AddDays(-1) },
            new Enrollment { Course = foreign, StudentUserId = student.Id, EnrolledAtUtc = start.AddDays(-1) });
        db.PracticeSubmissions.AddRange(
            new PracticeSubmission { Practice = inside, StudentUserId = student.Id, Score = 80, SubmittedAtUtc = start,
                Answers = [new() { PracticeQuestion = fillItem, TextAnswer = "wrong", IsCorrect = false },
                    new() { PracticeQuestion = mcqItem, SelectedOption = correct }] },
            new PracticeSubmission { Practice = excluded, StudentUserId = student.Id, Score = 20, SubmittedAtUtc = start.AddDays(1) },
            new PracticeSubmission { Practice = foreignPractice, StudentUserId = student.Id, Score = 1, SubmittedAtUtc = start });
        await db.SaveChangesAsync();
        var analytics = new TeacherAnalyticsQueryService(db, scope.ServiceProvider.GetRequiredService<IActiveUserValidator>(),
            new FixedClock(start.AddDays(2)));
        var result = (await analytics.GetAsync(teacher.Id, new() { From = new(2026, 11, 19), To = new(2026, 11, 19) })).Value!;
        Assert.Equal(80m, result.AveragePracticeScore);
        var missed = Assert.Single(result.MostMissedQuestions);
        Assert.Equal(fill.Id, missed.Id);
        Assert.Equal(1, missed.WrongCount);
        Assert.Equal(1, missed.AnswerCount);
        Assert.Equal(80m, Assert.Single(result.Students).PracticeScore);
        var bounded = (await analytics.GetAsync(teacher.Id, new() { To = DateOnly.MinValue })).Value!;
        Assert.Equal(new DateOnly(2000, 1, 1), bounded.Query.From);
        Assert.Equal(new DateOnly(2000, 1, 1), bounded.Query.To);
    }

    [Fact]
    public async Task Web_RejectsForeignCourse_AndStudentRole()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        Guid foreignId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = new Course { OwnerTeacherUserId = other.Id, Title = "Foreign" };
            db.Courses.Add(course);
            await db.SaveChangesAsync();
            foreignId = course.Id;
        }
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, teacher);
        using var own = await client.GetAsync("/TeacherAnalytics");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.True(own.Headers.CacheControl!.NoStore);
        var html = await own.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PaymentId", html);
        Assert.DoesNotContain("example.test", html);
        using var denied = await client.GetAsync("/TeacherAnalytics?CourseId=" + foreignId);
        Assert.Equal(HttpStatusCode.Found, denied.StatusCode);
        Assert.Contains("/Account/AccessDenied", denied.Headers.Location!.ToString());
        using var studentClient = IdentityTestHelpers.CreateClient(factory);
        using var studentLogin = await IdentityTestHelpers.LoginAsync(studentClient, student);
        using var studentResponse = await studentClient.GetAsync("/TeacherAnalytics");
        Assert.Equal(HttpStatusCode.Found, studentResponse.StatusCode);
        Assert.Contains("/Account/AccessDenied", studentResponse.Headers.Location!.ToString());
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
