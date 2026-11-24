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

public sealed class M2MigrationCompatibilityTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261119055627_AddEstudy2Features";
    private static readonly DateTimeOffset SavedAt = new(2026, 11, 18, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly PlanStart = new(2026, 11, 12);
    private readonly DatabaseFixture database = new();

    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task UpgradePreservesSevenDayPlanAndMcqHistoryWhileAllowingLongPlansAndTextAnswers()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(database.ConnectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var teacher = new ApplicationUser { Id = Guid.NewGuid(), UserName = "legacy-teacher", FullName = "Legacy teacher" };
        var student = new ApplicationUser { Id = Guid.NewGuid(), UserName = "legacy-student", FullName = "Legacy student" };
        db.Users.AddRange(teacher, student);
        await db.SaveChangesAsync();
        var legacy = await SeedLegacyAsync(db, teacher.Id, student.Id);

        await db.Database.MigrateAsync();
        var oldAnswer = await db.AttemptAnswers.AsNoTracking().Include(a => a.SelectedOption)
            .Include(a => a.Attempt).ThenInclude(a => a.SkillResults)
            .SingleAsync(a => a.Id == legacy.AnswerId);
        Assert.Equal(legacy.OptionId, oldAnswer.SelectedOptionId);
        Assert.True(oldAnswer.SelectedOption!.IsCorrect);
        Assert.Equal(SavedAt, oldAnswer.SavedAtUtc);
        Assert.Null(oldAnswer.IsCorrect);
        Assert.Null(oldAnswer.TextAnswer);
        Assert.Null(oldAnswer.StructuredAnswerJson);
        Assert.Equal(legacy.AttemptId, oldAnswer.AttemptId);
        Assert.Equal(100m, oldAnswer.Attempt.OverallScore);
        Assert.Equal(100m, Assert.Single(oldAnswer.Attempt.SkillResults).Score);
        var migratedActivity = await db.StudentActivityDays.SingleAsync(day => day.StudentUserId == student.Id);
        Assert.Equal(new DateOnly(2026, 11, 18), migratedActivity.ActivityDate);

        var oldPlan = await db.Set<LearningPlan>().AsNoTracking().Include(p => p.Items).SingleAsync(p => p.Id == legacy.PlanId);
        Assert.Equal(7, oldPlan.DurationDays);
        Assert.Equal(PlanStart, oldPlan.StartDate);
        Assert.Equal(legacy.AttemptId, oldPlan.SourceAssessmentAttemptId);
        Assert.Equal(LearningPlanStatus.Completed, oldPlan.Status);
        var oldItem = Assert.Single(oldPlan.Items);
        Assert.Equal(legacy.PlanItemId, oldItem.Id);
        Assert.Equal(7, oldItem.DayIndex);
        Assert.Equal(LearningPlanItemStatus.Completed, oldItem.Status);
        Assert.Equal(SavedAt, oldItem.CompletedAtUtc);

        var newAttempt = AddTextAttempt(db, teacher.Id, student.Id);
        var newPlan = new LearningPlan
        {
            StudentUserId = student.Id, StartDate = PlanStart.AddDays(7), DurationDays = 30,
            SourceAssessmentAttempt = newAttempt,
            Items = [new LearningPlanItem
            {
                DayIndex = 30, Position = 1, TargetType = LearningPlanTargetType.Practice,
                TargetId = Guid.NewGuid(), Title = "Final day", Skill = EnglishSkill.Grammar
            }]
        };
        db.Set<LearningPlan>().Add(newPlan);
        await db.SaveChangesAsync();

        var saved = await db.Set<LearningPlan>().AsNoTracking().Include(p => p.Items).SingleAsync(p => p.Id == newPlan.Id);
        Assert.Equal(30, saved.DurationDays);
        Assert.Equal(30, Assert.Single(saved.Items).DayIndex);
        var newAnswer = await db.AttemptAnswers.AsNoTracking().SingleAsync(a => a.AttemptId == newAttempt.Id);
        Assert.Null(newAnswer.SelectedOptionId);
        Assert.Equal(" lived ", newAnswer.TextAnswer);
        Assert.True(newAnswer.IsCorrect);
        Assert.Equal(2, await db.AttemptAnswers.CountAsync());

        var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var downgrade = await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync(PreviousMigration));
        Assert.Equal(51000, downgrade.Number);
        Assert.Contains("downgrade blocked", downgrade.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(appliedMigrations, await db.Database.GetAppliedMigrationsAsync());
        var preservedPlan = await db.Set<LearningPlan>().AsNoTracking().Include(p => p.Items).SingleAsync(p => p.Id == newPlan.Id);
        Assert.Equal(30, preservedPlan.DurationDays);
        Assert.Equal(30, Assert.Single(preservedPlan.Items).DayIndex);
        var preservedAnswer = await db.AttemptAnswers.AsNoTracking().SingleAsync(a => a.Id == newAnswer.Id);
        Assert.Null(preservedAnswer.SelectedOptionId);
        Assert.Equal(" lived ", preservedAnswer.TextAnswer);
        Assert.True(preservedAnswer.IsCorrect);
        Assert.Equal(2, await db.AttemptAnswers.CountAsync());
    }

    private static async Task<LegacyIds> SeedLegacyAsync(AppDbContext db, Guid teacher, Guid student)
    {
        var questionId = Guid.NewGuid();
        var assessmentId = Guid.NewGuid();
        var assessmentQuestionId = Guid.NewGuid();
        var ids = new LegacyIds(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        // Insert only columns present in M1. Current EF adds the extended question, answer and duration columns.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Questions] ([Id], [OwnerTeacherUserId], [Content], [QuestionType], [PrimarySkill], [Status], [ContentLockedAtUtc], [CreatedAtUtc])
            VALUES ({questionId}, {teacher}, N'Legacy MCQ', N'MultipleChoice', N'Grammar', N'Published', {SavedAt}, {SavedAt});
            INSERT INTO [QuestionOptions] ([Id], [QuestionId], [Content], [OrderIndex], [IsCorrect], [CreatedAtUtc])
            VALUES ({ids.OptionId}, {questionId}, N'Legacy answer', 0, 1, {SavedAt});
            INSERT INTO [Assessments] ([Id], [OwnerTeacherUserId], [Title], [AssessmentType], [DurationMinutes], [MaxAttempts], [Status], [CreatedAtUtc])
            VALUES ({assessmentId}, {teacher}, N'Legacy assessment', N'PracticeExam', 20, 3, N'Published', {SavedAt});
            INSERT INTO [AssessmentQuestions] ([Id], [AssessmentId], [QuestionId], [OrderIndex], [Points], [CreatedAtUtc])
            VALUES ({assessmentQuestionId}, {assessmentId}, {questionId}, 0, 2, {SavedAt});
            INSERT INTO [Attempts] ([Id], [StudentUserId], [AssessmentId], [Status], [StartedAtUtc], [DeadlineUtc], [FinalizedAtUtc], [FinalizationReason], [OverallScore], [Passed], [CreatedAtUtc])
            VALUES ({ids.AttemptId}, {student}, {assessmentId}, N'Graded', {SavedAt.AddMinutes(-10)}, {SavedAt.AddMinutes(10)}, {SavedAt}, N'ManualSubmit', 100, 1, {SavedAt});
            INSERT INTO [AttemptAnswers] ([Id], [AttemptId], [AssessmentQuestionId], [SelectedOptionId], [SavedAtUtc], [CreatedAtUtc])
            VALUES ({ids.AnswerId}, {ids.AttemptId}, {assessmentQuestionId}, {ids.OptionId}, {SavedAt}, {SavedAt});
            INSERT INTO [AttemptSkillResults] ([Id], [AttemptId], [Skill], [Score], [QuestionCount], [CreatedAtUtc])
            VALUES ({Guid.NewGuid()}, {ids.AttemptId}, N'Grammar', 100, 1, {SavedAt});
            INSERT INTO [LearningPlans] ([Id], [StudentUserId], [SourceAssessmentAttemptId], [StartDate], [TimezoneId], [Status], [CreatedAtUtc])
            VALUES ({ids.PlanId}, {student}, {ids.AttemptId}, {PlanStart}, N'Asia/Ho_Chi_Minh', N'Completed', {SavedAt.AddDays(-6)});
            INSERT INTO [LearningPlanItems] ([Id], [PlanId], [DayIndex], [TargetType], [TargetId], [Title], [EstimatedMinutes], [Skill], [Status], [Position], [CompletedAtUtc], [CreatedAtUtc])
            VALUES ({ids.PlanItemId}, {ids.PlanId}, 7, N'Assessment', {assessmentId}, N'Original goal', 20, N'Grammar', N'Completed', 1, {SavedAt}, {SavedAt.AddDays(-6)});
            """);
        return ids;
    }

    private static Attempt AddTextAttempt(AppDbContext db, Guid teacher, Guid student)
    {
        var link = new AssessmentQuestion
        {
            Points = 2m,
            Question = new Question
            {
                OwnerTeacherUserId = teacher, Content = "I ___ here.", QuestionType = QuestionType.FillBlank,
                PrimarySkill = EnglishSkill.Grammar,
                AcceptedAnswers = [new QuestionAcceptedAnswer { AnswerText = "lived" }]
            }
        };
        var assessment = new Assessment
        {
            OwnerTeacherUserId = teacher, Title = "FillBlank assessment", AssessmentType = AssessmentType.PracticeExam,
            DurationMinutes = 20, MaxAttempts = 3, Questions = [link]
        };
        var attempt = new Attempt
        {
            StudentUserId = student, Assessment = assessment, Status = AttemptStatus.Graded,
            StartedAtUtc = SavedAt, DeadlineUtc = SavedAt.AddMinutes(20), FinalizedAtUtc = SavedAt.AddMinutes(5),
            FinalizationReason = AttemptFinalizationReason.ManualSubmit, OverallScore = 100m,
            Answers = [new AttemptAnswer { AssessmentQuestion = link, TextAnswer = " lived ", IsCorrect = true }]
        };
        db.Attempts.Add(attempt);
        return attempt;
    }

    private sealed record LegacyIds(Guid AttemptId, Guid AnswerId, Guid OptionId, Guid PlanId, Guid PlanItemId);
}
