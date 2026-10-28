using System.Text;
using System.Text.Json;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using EnglishLearningPlatform.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Database ngẫu nhiên riêng từng test; không kết nối database làm việc của người dùng.
public sealed class QuestionDifficultyMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261015181741_AddQuestionAndStimulusContentLocks";
    private const string RemovalMigration = "20261023165551_RemoveQuestionDifficulty";
    private readonly DatabaseFixture _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(_database.ConnectionString).Options);

    [Fact]
    public async Task FreshDatabase_RemovesOnlyDifficulty_AndKeepsTheRemainingQuestionMapping()
    {
        var migration = new RemoveQuestionDifficulty();
        var drop = Assert.IsType<DropColumnOperation>(Assert.Single(migration.UpOperations));
        Assert.Equal("Questions", drop.Table);
        Assert.Equal("Difficulty", drop.Name);
        var add = Assert.IsType<AddColumnOperation>(Assert.Single(migration.DownOperations));
        Assert.Equal("Questions", add.Table);
        Assert.Equal("Difficulty", add.Name);
        Assert.Equal("nvarchar(32)", add.ColumnType);
        Assert.True(add.IsNullable);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Contains(RemovalMigration, await db.Database.GetAppliedMigrationsAsync());
        var questionType = db.Model.FindEntityType(typeof(Question))!;
        Assert.Equal(new[] { "Content", "ContentLockedAtUtc", "CreatedAtUtc", "Explanation", "Id", "Level",
                "OwnerTeacherUserId", "PrimarySkill", "Status", "StimulusId", "UpdatedAtUtc" },
            questionType.GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.False(await HasDifficultyAsync(db));
    }

    [Fact]
    public async Task UpgradeAndRollback_PreserveEveryOtherColumnAndAllExistingRowsIncludingResults()
    {
        await using var db = CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        Assert.True(await HasDifficultyAsync(db));
        var data = await SeedAsync(db);
        // Tạo giá trị cũ bằng SQL vì thuộc tính đã được bỏ khỏi model hiện tại.
        await db.Database.ExecuteSqlRawAsync("UPDATE dbo.Questions SET Difficulty = CASE " +
            "WHEN [Status] = N'Archived' THEN N'Hard' WHEN [StimulusId] IS NOT NULL THEN N'Easy' ELSE NULL END");
        var before = await SnapshotAsync(db);
        var scoreBefore = await ScoreAsync(db, data);
        Assert.Equal(new ScoringResult(2.5m, 2.5m, 100m), scoreBefore);

        await migrator.MigrateAsync(RemovalMigration);
        Assert.False(await HasDifficultyAsync(db));
        Assert.Equal(before, await SnapshotAsync(db));
        Assert.Equal(scoreBefore, await ScoreAsync(db, data));

        await migrator.MigrateAsync(PreviousMigration);
        Assert.True(await HasDifficultyAsync(db));
        Assert.Equal(before, await SnapshotAsync(db));
        // Down chỉ khôi phục cấu trúc cột; giá trị cũ phải phục hồi từ bản sao lưu.
        await db.Database.OpenConnectionAsync();
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM dbo.Questions WHERE Difficulty IS NOT NULL";
            Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
        await migrator.MigrateAsync(RemovalMigration);
        Assert.Equal(before, await SnapshotAsync(db));
        Assert.Equal(scoreBefore, await ScoreAsync(db, data));
    }

    private static async Task<bool> HasDifficultyAsync(AppDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Questions') AND name = N'Difficulty'";
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<string> SnapshotAsync(AppDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        var connection = db.Database.GetDbConnection();
        var tables = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var schema = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT s.name, t.name, c.name, ty.name, c.max_length, c.precision, c.scale,
                       c.is_nullable, c.is_identity, ISNULL(d.definition, N''), ISNULL(cc.definition, N'')
                FROM sys.tables t
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                JOIN sys.columns c ON c.object_id = t.object_id
                JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                LEFT JOIN sys.default_constraints d ON d.object_id = c.default_object_id
                LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
                WHERE t.is_ms_shipped = 0 AND t.name <> N'__EFMigrationsHistory'
                  AND NOT (s.name = N'dbo' AND t.name = N'Questions' AND c.name = N'Difficulty')
                ORDER BY s.name, t.name, c.column_id
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var table = $"[{reader.GetString(0)}].[{reader.GetString(1)}]";
                if (!tables.ContainsKey(table)) tables[table] = [];
                tables[table].Add($"[{reader.GetString(2)}]");
                schema.Add(JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue)));
            }
        }
        var rows = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (table, columns) in tables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {string.Join(", ", columns)} FROM {table} FOR JSON PATH, INCLUDE_NULL_VALUES";
            await using var reader = await command.ExecuteReaderAsync();
            var json = new StringBuilder();
            while (await reader.ReadAsync()) json.Append(reader.GetString(0));
            using var document = JsonDocument.Parse(json.Length == 0 ? "[]" : json.ToString());
            rows[table] = document.RootElement.EnumerateArray().Select(row => row.GetRawText())
                .OrderBy(row => row, StringComparer.Ordinal).ToArray();
        }
        return JsonSerializer.Serialize(new { Schema = schema, Rows = rows });
    }

    private static async Task<ScoringResult> ScoreAsync(AppDbContext db, SeedData data)
    {
        db.ChangeTracker.Clear();
        var assessment = await db.Assessments.Include(a => a.Questions).ThenInclude(aq => aq.Question)
            .ThenInclude(q => q.Options).SingleAsync(a => a.Id == data.AssessmentId);
        return new ExamScoringService().Score(assessment, [new QuestionResponse(data.QuestionId, data.OptionId)]);
    }

    private static async Task<SeedData> SeedAsync(AppDbContext db)
    {
        var teacher = new ApplicationUser { FullName = "Teacher", UserName = "migration-teacher@example.test" };
        var student = new ApplicationUser { FullName = "Student", UserName = "migration-student@example.test" };
        db.Users.AddRange(teacher, student);
        var passage = new Stimulus { OwnerTeacherUserId = teacher.Id, StimulusType = StimulusType.Passage,
            Title = "Existing passage", ContentText = "Read this existing passage." };
        var question = NewQuestion(teacher.Id, "Existing locked question");
        question.Stimulus = passage;
        var lockedAt = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
        question.LockContent(lockedAt);
        passage.LockContent(lockedAt);
        var archived = NewQuestion(teacher.Id, "Archived question");
        archived.Status = QuestionStatus.Archived;
        db.Questions.AddRange(question, NewQuestion(teacher.Id, "Unused draft"), archived);

        var assessmentQuestion = new AssessmentQuestion { Question = question, Points = 2.5m, OrderIndex = 0 };
        var assessment = new Assessment { OwnerTeacherUserId = teacher.Id, Title = "Existing assessment",
            AssessmentType = AssessmentType.PracticeExam, DurationMinutes = 15, MaxAttempts = 3,
            PassingScore = 70m, Status = AssessmentStatus.Published, Questions = [assessmentQuestion] };
        db.Assessments.Add(assessment);
        var option = question.Options.Single(o => o.IsCorrect);
        var attempt = new Attempt { Assessment = assessment, StudentUserId = student.Id,
            Status = AttemptStatus.Graded, StartedAtUtc = lockedAt, DeadlineUtc = lockedAt.AddMinutes(15),
            FinalizedAtUtc = lockedAt.AddMinutes(10), FinalizationReason = AttemptFinalizationReason.ManualSubmit,
            OverallScore = 100m, Passed = true,
            Answers = [new AttemptAnswer { AssessmentQuestion = assessmentQuestion, SelectedOption = option }],
            SkillResults = [new AttemptSkillResult { Skill = EnglishSkill.Reading, Score = 100m, QuestionCount = 1 }] };
        db.Attempts.Add(attempt);

        var practiceQuestion = new PracticeQuestion { Question = question, OrderIndex = 0 };
        var practice = new Practice { OwnerTeacherUserId = teacher.Id, Title = "Existing practice",
            Module = new Module { Title = "Existing module", Course = new Course {
                OwnerTeacherUserId = teacher.Id, Title = "Existing course", Status = CourseStatus.Published } },
            Status = PracticeStatus.Published, Questions = [practiceQuestion] };
        db.Modules.Add(practice.Module!);
        await db.SaveChangesAsync();
        // Schema lịch sử chưa có Version: chèn các cột thật của thời điểm đó bằng SQL tham số.
        // Các entity khác vẫn được seed bằng EF; không giả lập logic migration đang kiểm tra.
        var submissionId = Guid.NewGuid();
        var answerId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.Practices (Id, OwnerTeacherUserId, ModuleId, Title, Status, CreatedAtUtc)
            VALUES ({practice.Id}, {teacher.Id}, {practice.Module!.Id}, {practice.Title}, N'Published', {lockedAt});
            INSERT INTO dbo.PracticeQuestions (Id, PracticeId, QuestionId, OrderIndex, CreatedAtUtc)
            VALUES ({practiceQuestion.Id}, {practice.Id}, {question.Id}, 0, {lockedAt});
            INSERT INTO dbo.PracticeSubmissions (Id, PracticeId, StudentUserId, Score, SubmittedAtUtc, CreatedAtUtc)
            VALUES ({submissionId}, {practice.Id}, {student.Id}, 100, {lockedAt}, {lockedAt});
            INSERT INTO dbo.PracticeAnswers (Id, PracticeSubmissionId, PracticeQuestionId, SelectedOptionId, CreatedAtUtc)
            VALUES ({answerId}, {submissionId}, {practiceQuestion.Id}, {option.Id}, {lockedAt});
            """);
        return new SeedData(assessment.Id, question.Id, option.Id);
    }

    private static Question NewQuestion(Guid teacherId, string content) => new()
    {
        OwnerTeacherUserId = teacherId, Content = content, PrimarySkill = EnglishSkill.Reading,
        Level = "B1", Explanation = "Existing feedback.",
        Options = [new QuestionOption { Content = "Correct", IsCorrect = true, OrderIndex = 0 },
            new QuestionOption { Content = "Incorrect", IsCorrect = false, OrderIndex = 1 }]
    };

    private sealed record SeedData(Guid AssessmentId, Guid QuestionId, Guid OptionId);
}
