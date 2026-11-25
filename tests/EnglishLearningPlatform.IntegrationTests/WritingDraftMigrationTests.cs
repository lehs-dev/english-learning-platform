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
        var first = new WritingAssignment { Course = course, Title = "Submitted", Prompt = "Write one paragraph." };
        var second = new WritingAssignment { Course = course, Title = "In progress", Prompt = "Write a reply." };
        db.Users.AddRange(teacher, student);
        db.WritingAssignments.AddRange(first, second);
        var submission = new WritingSubmission
        {
            Assignment = first, StudentUserId = student.Id, Response = "Existing finished response.",
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
            AssignmentId = second.Id, StudentUserId = student.Id, Response = "Unfinished response.",
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
}
