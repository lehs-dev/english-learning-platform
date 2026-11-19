using EnglishLearningPlatform.Application.Gamification;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Gamification;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class GamificationRewardTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task Practice_RequiresServerEvidence_AndCapsBothRewardsPerVietnamDay()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.Parse("2026-11-19T16:59:59Z");
        var clock = new MutableClock(now);
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "XP course", Status = CourseStatus.Published };
        var module = new Module { Course = course, Title = "Module", Visibility = ModuleVisibility.Visible };
        var practice = new Practice { Module = module, OwnerTeacherUserId = teacher.Id, Title = "XP practice", Status = PracticeStatus.Published };
        var submission = new PracticeSubmission { Practice = practice, StudentUserId = student.Id, Score = 79, SubmittedAtUtc = now };
        db.Add(submission);
        await db.SaveChangesAsync();
        var rewards = new GamificationService(db, scope.ServiceProvider.GetRequiredService<IActiveUserValidator>(), clock);
        Assert.False(await rewards.AwardXpAsync(student.Id, GamificationEventType.PracticeSubmitted, Guid.NewGuid(), 5));
        Assert.False(await rewards.AwardXpAsync(student.Id, GamificationEventType.PracticeSubmitted, practice.Id, 500));
        Assert.True(await rewards.AwardXpAsync(student.Id, GamificationEventType.PracticeSubmitted, practice.Id, 5));
        Assert.False(await rewards.AwardXpAsync(student.Id, GamificationEventType.PracticeSubmitted, practice.Id, 5));
        Assert.False(await rewards.AwardXpAsync(student.Id, GamificationEventType.PracticeHighScorer, practice.Id, 10));
        submission.Score = 80;
        await db.SaveChangesAsync();
        Assert.True(await rewards.AwardXpAsync(student.Id, GamificationEventType.PracticeHighScorer, practice.Id, 10));
        Assert.False(await rewards.AwardXpAsync(student.Id, GamificationEventType.PracticeHighScorer, practice.Id, 10));
        Assert.Equal(15, (await rewards.GetSummaryAsync(student.Id)).Value!.TotalXp);
        clock.UtcNow = now.AddSeconds(1);
        Assert.False(await rewards.AwardXpAsync(student.Id, GamificationEventType.PracticeSubmitted, practice.Id, 5));
        submission.SubmittedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync();
        Assert.True(await rewards.AwardXpAsync(student.Id, GamificationEventType.PracticeSubmitted, practice.Id, 5));
        Assert.Equal(2, (await rewards.GetSummaryAsync(student.Id)).Value!.CurrentStreak);
        Assert.Equal(3, await db.GamificationEvents.CountAsync(e => e.StudentUserId == student.Id));
    }

    [Fact]
    public async Task ConcurrentDuplicateAwards_CreateOneEvent()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        Guid practiceId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Concurrent XP", Status = CourseStatus.Published };
            var module = new Module { Course = course, Title = "Module", Visibility = ModuleVisibility.Visible };
            var practice = new Practice { Module = module, OwnerTeacherUserId = teacher.Id, Title = "Practice", Status = PracticeStatus.Published };
            db.PracticeSubmissions.Add(new() { Practice = practice, StudentUserId = student.Id, Score = 100 });
            await db.SaveChangesAsync();
            practiceId = practice.Id;
        }
        async Task<bool> Award()
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IGamificationService>()
                .AwardXpAsync(student.Id, GamificationEventType.PracticeSubmitted, practiceId, 5);
        }
        var results = await Task.WhenAll(Award(), Award(), Award());
        Assert.Single(results, r => r);
        using var check = factory.Services.CreateScope();
        var summary = await check.ServiceProvider.GetRequiredService<IGamificationService>().GetSummaryAsync(student.Id);
        Assert.Equal(5, summary.Value!.TotalXp);
    }

    [Fact]
    public async Task LessonLifetimeReward_CombinesWithPracticeForDailyGoal_WithoutCountingItAgainTomorrow()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new MutableClock(DateTimeOffset.Parse("2026-11-19T16:59:59Z"));
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Daily lesson goal", Status = CourseStatus.Published };
        var module = new Module { Course = course, Title = "Module" };
        var lesson = new Lesson { Module = module, Title = "Lesson", Status = LessonStatus.Published };
        var enrollment = new Enrollment { Course = course, StudentUserId = student.Id };
        var practice = new Practice { Module = module, OwnerTeacherUserId = teacher.Id, Title = "Practice", Status = PracticeStatus.Published };
        var submission = new PracticeSubmission { Practice = practice, StudentUserId = student.Id, Score = 70, SubmittedAtUtc = clock.UtcNow };
        db.LessonProgressEntries.Add(new()
        {
            Enrollment = enrollment, Lesson = lesson, IsCompleted = true, UpdatedAtUtc = clock.UtcNow
        });
        db.PracticeSubmissions.Add(submission);
        await db.SaveChangesAsync();
        var rewards = new GamificationService(db, scope.ServiceProvider.GetRequiredService<IActiveUserValidator>(), clock);

        // Award the lifetime event last: its key must not be reused as the daily-goal key.
        Assert.True(await rewards.AwardXpAsync(student.Id, GamificationEventType.PracticeSubmitted, practice.Id, 5));
        Assert.True(await rewards.AwardXpAsync(student.Id, GamificationEventType.LessonCompleted, lesson.Id, 10));
        Assert.False(await rewards.AwardXpAsync(student.Id, GamificationEventType.LessonCompleted, lesson.Id, 10));
        var firstDay = (await rewards.GetSummaryAsync(student.Id)).Value!;
        Assert.Equal(25, firstDay.TotalXp);
        Assert.Single(await db.StudentAchievements.Where(a => a.StudentUserId == student.Id && a.BadgeCode == "first-lesson").ToListAsync());
        var goal = await db.GamificationEvents.SingleAsync(e => e.StudentUserId == student.Id && e.EventType == GamificationEventType.DailyGoalReached);
        Assert.Equal("2026-11-19", goal.WindowKey);
        Assert.Equal(10, goal.Xp);

        clock.UtcNow = clock.UtcNow.AddSeconds(1); // Midnight in Vietnam, still November 19 UTC.
        Assert.False(await rewards.AwardXpAsync(student.Id, GamificationEventType.LessonCompleted, lesson.Id, 10));
        Assert.False(await rewards.AwardXpAsync(student.Id, GamificationEventType.PracticeSubmitted, practice.Id, 5));
        submission.SubmittedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync();
        Assert.True(await rewards.AwardXpAsync(student.Id, GamificationEventType.PracticeSubmitted, practice.Id, 5));
        Assert.Equal(30, (await rewards.GetSummaryAsync(student.Id)).Value!.TotalXp);
        Assert.Equal(1, await db.GamificationEvents.CountAsync(e => e.StudentUserId == student.Id && e.EventType == GamificationEventType.DailyGoalReached));
        Assert.Equal(1, await db.GamificationEvents.CountAsync(e => e.StudentUserId == student.Id && e.EventType == GamificationEventType.LessonCompleted));
        Assert.Equal(1, await db.StudentAchievements.CountAsync(a => a.StudentUserId == student.Id && a.BadgeCode == "first-lesson"));
        Assert.Equal(2, (await rewards.GetSummaryAsync(student.Id)).Value!.CurrentStreak);
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
