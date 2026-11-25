using System.Text;
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

// Each test instance owns a GUID database on ELP_TEST_SQLSERVER; no application/demo database is migrated.
public sealed class EstudyMigrationCompatibilityTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261028171139_AddPracticeVersionsAndSessions";
    private const string EstudyMigration = "20261119055627_AddEstudy2Features";
    private static readonly DateTimeOffset Created = new(2026, 10, 20, 1, 2, 3, TimeSpan.Zero);
    private static readonly DateTimeOffset Updated = Created.AddHours(2);
    private static readonly DateTimeOffset Submitted = Created.AddDays(1);
    private readonly DatabaseFixture _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();
    private AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(_database.ConnectionString).Options);

    [Fact]
    public async Task Upgrade_preserves_every_legacy_history_field_and_defaults_questions_to_mcq()
    {
        await using var db = CreateDb();
        var data = await SeedLegacyAsync(db);
        var before = await LegacySnapshotAsync(db);
        await db.GetService<IMigrator>().MigrateAsync(EstudyMigration);
        AssertSnapshotsEqual(before, await LegacySnapshotAsync(db));
        // The current model also maps later columns. Verify the legacy projection
        // again after applying them before round-tripping through the current model.
        await db.Database.MigrateAsync();
        AssertSnapshotsEqual(before, await LegacySnapshotAsync(db));
        db.ChangeTracker.Clear();

        var questions = await db.Questions.AsNoTracking().OrderBy(q => q.Id).ToListAsync();
        Assert.Equal(2, questions.Count);
        Assert.All(questions, q =>
        {
            Assert.Equal(QuestionType.MultipleChoice, q.QuestionType);
            Assert.Equal(Updated, q.ContentLockedAtUtc);
        });
        var submission = await db.PracticeSubmissions.AsNoTracking().Include(s => s.Answers).SingleAsync();
        Assert.Equal(data.Submission, submission.Id);
        Assert.Equal(50m, submission.Score);
        Assert.Equal(1, submission.Version);
        Assert.Equal(Submitted, submission.SubmittedAtUtc);
        Assert.Equal(data.Answers.Order(), submission.Answers.Select(a => a.Id).Order());
        Assert.Equal(data.SelectedOptions.Order(), submission.Answers.Select(a => a.SelectedOptionId!.Value).Order());
        Assert.All(submission.Answers, a =>
        {
            Assert.Null(a.IsCorrect); // Existing MCQ evaluation continues to derive correctness from the preserved option.
            Assert.Null(a.TextAnswer);
            Assert.Null(a.StructuredAnswerJson);
        });
        var attempt = await db.Attempts.AsNoTracking().Include(a => a.Answers).Include(a => a.SkillResults).SingleAsync();
        Assert.Equal(data.Attempt, attempt.Id);
        Assert.Equal(75m, attempt.OverallScore);
        Assert.True(attempt.Passed);
        Assert.Equal(AttemptStatus.Graded, attempt.Status);
        Assert.Equal(2, attempt.Answers.Count);
        Assert.Equal(data.SelectedOptions.Order(), attempt.Answers.Select(a => a.SelectedOptionId!.Value).Order());
        Assert.All(attempt.Answers, answer => Assert.Null(answer.IsCorrect));
        Assert.Equal(75m, Assert.Single(attempt.SkillResults).Score);
        Assert.Equal(2, attempt.SkillResults.Single().QuestionCount);
        Assert.Empty(await db.Set<PracticeSubmissionHistory>().ToListAsync());
        Assert.Empty(await db.Set<PracticeAnswerHistory>().ToListAsync());
        Assert.Empty(await db.Set<FlashcardDeck>().ToListAsync());
        Assert.Contains(EstudyMigration, await db.Database.GetAppliedMigrationsAsync());

        // The widened FK also accepts a real FillBlank answer without inventing an option ID.
        var fill = new Question
        {
            OwnerTeacherUserId = data.Teacher, Content = "I ___ here before.", PrimarySkill = EnglishSkill.Grammar,
            QuestionType = QuestionType.FillBlank,
            AcceptedAnswers = [new QuestionAcceptedAnswer { AnswerText = "lived", OrderIndex = 0 }]
        };
        var link = new PracticeQuestion { Question = fill, Version = 1, OrderIndex = 0 };
        var newPractice = new Practice
        {
            OwnerTeacherUserId = data.Teacher, ModuleId = data.Module, Title = "New FillBlank practice",
            Status = PracticeStatus.Published, Questions = [link]
        };
        var newAnswer = new PracticeAnswer { PracticeQuestion = link, TextAnswer = " lived ", IsCorrect = true };
        db.PracticeSubmissions.Add(new PracticeSubmission
        {
            StudentUserId = data.Student, Practice = newPractice, Score = 100m, Answers = [newAnswer]
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var saved = await db.PracticeAnswers.AsNoTracking().SingleAsync(a => a.Id == newAnswer.Id);
        Assert.Null(saved.SelectedOptionId);
        Assert.Equal(" lived ", saved.TextAnswer);
        Assert.True(saved.IsCorrect);
        Assert.Equal(2, await db.PracticeAnswers.CountAsync(a => a.PracticeSubmissionId == data.Submission));
        Assert.Equal(2, await db.AttemptAnswers.CountAsync(a => a.AttemptId == data.Attempt));
    }

    [Fact]
    public async Task Legacy_mcq_only_database_can_downgrade_and_upgrade_without_changing_rows()
    {
        await using var db = CreateDb();
        await SeedLegacyAsync(db);
        var before = await LegacySnapshotAsync(db);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(EstudyMigration);
        await migrator.MigrateAsync(PreviousMigration);
        AssertSnapshotsEqual(before, await LegacySnapshotAsync(db));
        Assert.DoesNotContain(EstudyMigration, await db.Database.GetAppliedMigrationsAsync());
        await migrator.MigrateAsync(EstudyMigration);
        AssertSnapshotsEqual(before, await LegacySnapshotAsync(db));
        Assert.Equal(2, await db.Questions.CountAsync(q => q.QuestionType == QuestionType.MultipleChoice));
    }

    [Theory]
    [InlineData("deck")]
    [InlineData("history")]
    public async Task Downgrade_refuses_to_erase_new_learning_data_and_leaves_all_records_intact(string newData)
    {
        await using var db = CreateDb();
        var data = await SeedLegacyAsync(db);
        await db.GetService<IMigrator>().MigrateAsync(EstudyMigration);
        Guid newId;
        if (newData == "deck")
        {
            var deck = new FlashcardDeck
            {
                OwnerTeacherUserId = data.Teacher, CourseId = data.Course,
                Title = "Must survive a rejected downgrade", Status = FlashcardDeckStatus.Draft
            };
            newId = deck.Id;
            db.Set<FlashcardDeck>().Add(deck);
        }
        else
        {
            var history = new PracticeSubmissionHistory
            {
                SourceSubmissionId = data.Submission, SourceCreatedAtUtc = Created, SourceUpdatedAtUtc = Updated,
                StudentUserId = data.Student, PracticeId = data.Practice, Score = 50m, Version = 1,
                SubmittedAtUtc = Submitted,
                Answers = [new PracticeAnswerHistory
                {
                    SourceAnswerId = data.Answers[0], SourceCreatedAtUtc = Created, SourceUpdatedAtUtc = Updated,
                    PracticeQuestionId = data.PracticeQuestions[0], SelectedOptionId = data.SelectedOptions[0],
                    QuestionContent = "Original question one", SelectedAnswer = "Correct one",
                    CorrectAnswersJson = "[\"Correct one\"]", IsCorrect = true
                }]
            };
            newId = history.Id;
            db.Set<PracticeSubmissionHistory>().Add(history);
        }
        await db.SaveChangesAsync();
        var before = await LegacySnapshotAsync(db);

        var error = await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync(PreviousMigration));
        Assert.Equal(51000, error.Number);
        Assert.Contains("downgrade blocked", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(EstudyMigration, await db.Database.GetAppliedMigrationsAsync());
        AssertSnapshotsEqual(before, await LegacySnapshotAsync(db));
        db.ChangeTracker.Clear();
        if (newData == "deck")
            Assert.Equal("Must survive a rejected downgrade", (await db.Set<FlashcardDeck>().SingleAsync(d => d.Id == newId)).Title);
        else
        {
            var saved = await db.Set<PracticeSubmissionHistory>().Include(h => h.Answers).SingleAsync(h => h.Id == newId);
            Assert.Equal(data.Submission, saved.SourceSubmissionId);
            Assert.Equal(50m, saved.Score);
            Assert.Equal(data.Answers[0], Assert.Single(saved.Answers).SourceAnswerId);
        }
    }

    private static async Task<LegacyData> SeedLegacyAsync(AppDbContext db)
    {
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var suffix = Guid.NewGuid().ToString("N");
        var teacher = new ApplicationUser { Id = Guid.NewGuid(), UserName = "migration-teacher-" + suffix, FullName = "Legacy teacher", CreatedAtUtc = Created };
        var student = new ApplicationUser { Id = Guid.NewGuid(), UserName = "migration-student-" + suffix, FullName = "Legacy student", CreatedAtUtc = Created };
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Legacy course", Status = CourseStatus.Published, CreatedAtUtc = Created, UpdatedAtUtc = Updated };
        var module = new Module { Course = course, Title = "Legacy module", CreatedAtUtc = Created, UpdatedAtUtc = Updated };
        db.Users.AddRange(teacher, student);
        db.Modules.Add(module);
        db.Enrollments.Add(new Enrollment { StudentUserId = student.Id, Course = course, EnrolledAtUtc = Created, CreatedAtUtc = Created, UpdatedAtUtc = Updated });
        await db.SaveChangesAsync();
        Guid[] questionIds = [Guid.NewGuid(), Guid.NewGuid()];
        Guid[] selectedOptions = [Guid.NewGuid(), Guid.NewGuid()];
        Guid correctTwo = Guid.NewGuid(), wrongOne = Guid.NewGuid();
        // Current EF Question mappings include QuestionType, and the Option guard reads that model.
        // Seed both tables with their actual pre-Estudy columns so this is a real schema compatibility test.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Questions] ([Id], [OwnerTeacherUserId], [Content], [PrimarySkill], [Level], [Explanation], [Status], [ContentLockedAtUtc], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES ({questionIds[0]}, {teacher.Id}, N'Original question one', N'Reading', N'A2', N'Original explanation one', N'Published', {Updated}, {Created}, {Updated}),
                   ({questionIds[1]}, {teacher.Id}, N'Original question two', N'Reading', N'A2', N'Original explanation two', N'Published', {Updated}, {Created}, {Updated});
            INSERT INTO [QuestionOptions] ([Id], [QuestionId], [Content], [OrderIndex], [IsCorrect], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES ({selectedOptions[0]}, {questionIds[0]}, N'Correct one', 0, 1, {Created}, {Updated}),
                   ({wrongOne}, {questionIds[0]}, N'Wrong one', 1, 0, {Created}, {Updated}),
                   ({correctTwo}, {questionIds[1]}, N'Correct two', 0, 1, {Created}, {Updated}),
                   ({selectedOptions[1]}, {questionIds[1]}, N'Wrong two', 1, 0, {Created}, {Updated});
            """);
        var practice = new Practice
        {
            OwnerTeacherUserId = teacher.Id, ModuleId = module.Id, Title = "Legacy practice", Status = PracticeStatus.Published,
            Version = 1, CreatedAtUtc = Created, UpdatedAtUtc = Updated,
            Questions = questionIds.Select((id, index) => new PracticeQuestion
            { QuestionId = id, OrderIndex = index, Version = 1, CreatedAtUtc = Created, UpdatedAtUtc = Updated }).ToList()
        };
        var submission = new PracticeSubmission
        {
            StudentUserId = student.Id, Practice = practice, Score = 50m, Version = 1,
            SubmittedAtUtc = Submitted, CreatedAtUtc = Created, UpdatedAtUtc = Updated
        };
        var assessment = new Assessment
        {
            OwnerTeacherUserId = teacher.Id, CourseId = course.Id, Title = "Legacy assessment", AssessmentType = AssessmentType.PracticeExam,
            DurationMinutes = 30, MaxAttempts = 3, PassingScore = 60m, Status = AssessmentStatus.Published,
            CreatedAtUtc = Created, UpdatedAtUtc = Updated,
            Questions = questionIds.Select((id, index) => new AssessmentQuestion
            { QuestionId = id, OrderIndex = index, Points = index == 0 ? 3m : 1m, CreatedAtUtc = Created, UpdatedAtUtc = Updated }).ToList()
        };
        var attempt = new Attempt
        {
            StudentUserId = student.Id, Assessment = assessment, Status = AttemptStatus.Graded,
            StartedAtUtc = Submitted.AddMinutes(-10), DeadlineUtc = Submitted.AddMinutes(20), FinalizedAtUtc = Submitted,
            FinalizationReason = AttemptFinalizationReason.ManualSubmit, OverallScore = 75m, Passed = true,
            CreatedAtUtc = Created, UpdatedAtUtc = Updated,
            SkillResults = [new AttemptSkillResult { Skill = EnglishSkill.Reading, Score = 75m, QuestionCount = 2, CreatedAtUtc = Created, UpdatedAtUtc = Updated }]
        };
        db.PracticeSubmissions.Add(submission);
        db.Attempts.Add(attempt);
        db.PracticeSessions.Add(new PracticeSession
        { StudentUserId = student.Id, Practice = practice, Version = 1, SubmittedAtUtc = Submitted, CreatedAtUtc = Created, UpdatedAtUtc = Updated });
        await db.SaveChangesAsync();
        Guid[] answers = [Guid.NewGuid(), Guid.NewGuid()];
        var practiceQuestionIds = practice.Questions.Select(q => q.Id).ToArray();
        var assessmentQuestionIds = assessment.Questions.Select(q => q.Id).ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [PracticeAnswers] ([Id], [PracticeSubmissionId], [PracticeQuestionId], [SelectedOptionId], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES ({answers[0]}, {submission.Id}, {practiceQuestionIds[0]}, {selectedOptions[0]}, {Created}, {Updated}),
                   ({answers[1]}, {submission.Id}, {practiceQuestionIds[1]}, {selectedOptions[1]}, {Created}, {Updated});
            INSERT INTO [AttemptAnswers] ([Id], [AttemptId], [AssessmentQuestionId], [SelectedOptionId], [SavedAtUtc], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES ({Guid.NewGuid()}, {attempt.Id}, {assessmentQuestionIds[0]}, {selectedOptions[0]}, {Submitted}, {Created}, {Updated}),
                   ({Guid.NewGuid()}, {attempt.Id}, {assessmentQuestionIds[1]}, {selectedOptions[1]}, {Submitted}, {Created}, {Updated});
            """);
        db.ChangeTracker.Clear();
        return new(teacher.Id, student.Id, course.Id, module.Id, practice.Id, submission.Id, attempt.Id, answers, selectedOptions, practiceQuestionIds);
    }

    private static async Task<Dictionary<string, string>> LegacySnapshotAsync(AppDbContext db)
    {
        // SQL JSON preserves nulls, IDs, timestamp offsets and decimal values; column lists omit only new columns.
        var projections = new Dictionary<string, string>
        {
            ["Questions"] = "[Id], [OwnerTeacherUserId], [StimulusId], [Content], [PrimarySkill], [Level], [Explanation], [Status], [ContentLockedAtUtc], [CreatedAtUtc], [UpdatedAtUtc]",
            ["PracticeAnswers"] = "[Id], [PracticeSubmissionId], [PracticeQuestionId], [SelectedOptionId], [CreatedAtUtc], [UpdatedAtUtc]",
            ["QuestionOptions"] = "*", ["Courses"] = "*", ["Modules"] = "*", ["Enrollments"] = "*",
            ["Practices"] = "*", ["PracticeQuestions"] = "*", ["PracticeSessions"] = "*", ["PracticeSubmissions"] = "*",
            ["Assessments"] = "*", ["AssessmentQuestions"] = "*", ["Attempts"] = "*", ["AttemptSkillResults"] = "*",
            ["AttemptAnswers"] = "[Id], [AttemptId], [AssessmentQuestionId], [SelectedOptionId], [SavedAtUtc], [CreatedAtUtc], [UpdatedAtUtc]"
        };
        var snapshot = new Dictionary<string, string>();
        await db.Database.OpenConnectionAsync();
        try
        {
            foreach (var (table, columns) in projections)
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = $"SELECT {columns} FROM [dbo].[{table}] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES";
                await using var reader = await command.ExecuteReaderAsync();
                var json = new StringBuilder();
                while (await reader.ReadAsync()) json.Append(reader.GetString(0));
                snapshot.Add(table, json.ToString());
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
        return snapshot;
    }

    private static void AssertSnapshotsEqual(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var (table, rows) in expected)
            Assert.True(actual.TryGetValue(table, out var saved) && rows == saved, $"Migration changed legacy {table} fields or row counts.");
    }

    private sealed record LegacyData(Guid Teacher, Guid Student, Guid Course, Guid Module, Guid Practice,
        Guid Submission, Guid Attempt, Guid[] Answers, Guid[] SelectedOptions, Guid[] PracticeQuestions);
}
