using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Kiểm tra nâng cấp dữ liệu thật từ schema cũ, chỉ trên SQL Server test riêng.
public sealed class PracticeVersionMigrationTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database = new();
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Upgrade_ExistingPracticeAndFeedbackBecomeVersionOne_WithoutChangingIdsOrScore()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_database.ConnectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261023165551_RemoveQuestionDifficulty");
        var teacher = new ApplicationUser { Id = Guid.NewGuid(), FullName = "Teacher", UserName = "migration-teacher" };
        var student = new ApplicationUser { Id = Guid.NewGuid(), FullName = "Student", UserName = "migration-student" };
        var module = new Module { Title = "Module", Course = new Course
            { Title = "Course", OwnerTeacherUserId = teacher.Id } };
        var question = new Question { OwnerTeacherUserId = teacher.Id, Content = "Original question",
            PrimarySkill = EnglishSkill.Reading };
        var option = new QuestionOption { Question = question, Content = "Original answer", IsCorrect = true };
        db.Users.AddRange(teacher, student);
        db.Modules.Add(module);
        db.QuestionOptions.Add(option);
        db.QuestionOptions.Add(new QuestionOption { Question = question, Content = "Wrong", OrderIndex = 1 });
        await db.SaveChangesAsync();
        Guid practiceId = Guid.NewGuid(), linkId = Guid.NewGuid(), submissionId = Guid.NewGuid(), answerId = Guid.NewGuid();
        var submittedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        // Chưa có cột Version: seed đúng cấu trúc trước khi nâng cấp.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.Practices (Id, OwnerTeacherUserId, ModuleId, Title, Status, CreatedAtUtc)
            VALUES ({practiceId}, {teacher.Id}, {module.Id}, N'Existing practice', N'Published', {submittedAt});
            INSERT INTO dbo.PracticeQuestions (Id, PracticeId, QuestionId, OrderIndex, CreatedAtUtc)
            VALUES ({linkId}, {practiceId}, {question.Id}, 0, {submittedAt});
            INSERT INTO dbo.PracticeSubmissions (Id, PracticeId, StudentUserId, Score, SubmittedAtUtc, CreatedAtUtc)
            VALUES ({submissionId}, {practiceId}, {student.Id}, 75.5, {submittedAt}, {submittedAt});
            INSERT INTO dbo.PracticeAnswers (Id, PracticeSubmissionId, PracticeQuestionId, SelectedOptionId, CreatedAtUtc)
            VALUES ({answerId}, {submissionId}, {linkId}, {option.Id}, {submittedAt});
            """);

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var practice = await db.Practices.Include(p => p.Questions).SingleAsync();
        var result = await db.PracticeSubmissions.Include(s => s.Answers).SingleAsync();
        Assert.Equal(1, practice.Version);
        Assert.Equal(linkId, Assert.Single(practice.Questions).Id);
        Assert.Equal(1, practice.Questions.Single().Version);
        Assert.Equal(submissionId, result.Id);
        Assert.Equal(1, result.Version);
        Assert.Equal(75.5m, result.Score);
        Assert.Equal(submittedAt, result.SubmittedAtUtc);
        Assert.Equal(answerId, Assert.Single(result.Answers).Id);
        Assert.Equal(option.Id, result.Answers.Single().SelectedOptionId);
        Assert.Empty(await db.PracticeSessions.ToListAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
