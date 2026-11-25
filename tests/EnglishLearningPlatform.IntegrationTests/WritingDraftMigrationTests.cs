using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class WritingDraftMigrationTests : IAsyncLifetime
{
    private const string BeforeDrafts = "20261124133902_AddCourseCollaborationAndActivity";
    private readonly DatabaseFixture database = new();

    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task UpgradePreservesPriorSubmissions_AndDowngradeRefusesToEraseDrafts()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeDrafts);

        var teacher = new ApplicationUser { Id = Guid.NewGuid(), UserName = "teacher", FullName = "Giảng viên" };
        var student = new ApplicationUser { Id = Guid.NewGuid(), UserName = "student", FullName = "Học viên" };
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Writing class", Status = CourseStatus.Published };
        db.Users.AddRange(teacher, student);
        db.Courses.Add(course);
        await db.SaveChangesAsync();

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [WritingAssignments] ([Id], [CourseId], [Title], [Prompt], [CreatedAtUtc])
            VALUES ({first}, {course.Id}, N'Submitted', N'Write one paragraph.', {created}),
                   ({second}, {course.Id}, N'In progress', N'Write a reply.', {created})
            """);
        var submission = new WritingSubmission
        {
            AssignmentId = first, StudentUserId = student.Id, Response = "Existing finished response.",
            Feedback = "Existing teacher feedback.", Score = 85, ReviewedAtUtc = DateTimeOffset.UtcNow
        };
        db.WritingSubmissions.Add(submission);
        await db.SaveChangesAsync();

        await migrator.MigrateAsync();
        Assert.Empty(await db.WritingSubmissionDrafts.ToListAsync());
        await migrator.MigrateAsync(BeforeDrafts);
        await migrator.MigrateAsync();

        var draft = new WritingSubmissionDraft
        {
            AssignmentId = second, StudentUserId = student.Id, Response = "Unfinished response.",
            SavedAtUtc = DateTimeOffset.UtcNow, Version = Guid.NewGuid()
        };
        db.WritingSubmissionDrafts.Add(draft);
        await db.SaveChangesAsync();

        var blocked = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(BeforeDrafts));
        Assert.Equal(51000, blocked.Number);
        db.ChangeTracker.Clear();
        Assert.Equal(draft.Response, (await db.WritingSubmissionDrafts.SingleAsync()).Response);
        var stored = await db.WritingSubmissions.AsNoTracking().SingleAsync();
        Assert.Equal(submission.Response, stored.Response);
        Assert.Equal(submission.Feedback, stored.Feedback);
        Assert.Equal(submission.Score, stored.Score);
    }

    [Fact]
    public async Task DowngradeRefusesToReleasePrivateAssignments()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString).Options);
        await db.Database.MigrateAsync();
        var teacher = new ApplicationUser { Id = Guid.NewGuid(), UserName = "teacher", FullName = "Giảng viên" };
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Writing class", Status = CourseStatus.Published };
        var draft = new WritingAssignment { Course = course, Title = "Draft", Prompt = "Draft prompt", IsDraft = true };
        var scheduled = new WritingAssignment
        {
            Course = course, Title = "Scheduled", Prompt = "Scheduled prompt", PublishAtUtc = DateTimeOffset.UtcNow.AddDays(1)
        };
        db.Users.Add(teacher);
        db.WritingAssignments.AddRange(draft, scheduled);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<SqlException>(() =>
            db.GetService<IMigrator>().MigrateAsync("20261124155333_AddWritingSubmissionDrafts"));
        Assert.Equal(51000, error.Number);
        db.ChangeTracker.Clear();
        Assert.True((await db.WritingAssignments.SingleAsync(row => row.Id == draft.Id)).IsDraft);
        Assert.Equal(scheduled.PublishAtUtc, (await db.WritingAssignments.SingleAsync(row => row.Id == scheduled.Id)).PublishAtUtc);
    }
}
