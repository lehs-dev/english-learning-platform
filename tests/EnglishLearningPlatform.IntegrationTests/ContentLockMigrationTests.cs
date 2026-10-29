using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Mỗi test có database riêng; không dùng database demo hoặc fixture của web.
public sealed class ContentLockMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261008035103_AddCheckoutOrderMetadata";
    private const string LockMigration = "20261015181741_AddQuestionAndStimulusContentLocks";
    private readonly DatabaseFixture _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(_database.ConnectionString).Options);

    // Dùng dữ liệu có đủ điều kiện khóa và không khóa trong cùng một lần nâng cấp.
    [Fact]
    public async Task Upgrade_LocksLegacyContentWithoutLockingUnusedDraftsOrChangingResults()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var teacher = await AddUserAsync(db, AppRoles.Teacher);
        var student = await AddUserAsync(db, AppRoles.Student);
        var shared = new Stimulus { OwnerTeacherUserId = teacher.Id,
            StimulusType = StimulusType.Passage, ContentText = "Shared passage" };
        var draftOnly = new Stimulus { OwnerTeacherUserId = teacher.Id,
            StimulusType = StimulusType.Passage, ContentText = "Draft passage" };
        string[] cases = { "Unused draft", "Published", "Archived", "Published assessment",
            "Unpublished practice", "Attempt exists", "Submission exists", "Draft assessment", "Draft practice" };
        var questions = new List<Question>();
        for (int index = 0; index < cases.Length; index++)
        {
            var question = NewQuestion(teacher.Id, cases[index]);
            question.Status = index == 1 ? QuestionStatus.Published
                : index == 2 ? QuestionStatus.Archived : QuestionStatus.Draft;
            questions.Add(question);
        }
        questions[0].Stimulus = shared;
        questions[3].Stimulus = shared;
        questions[7].Stimulus = draftOnly;
        questions[8].Stimulus = draftOnly;
        db.Questions.AddRange(questions);

        var started = new DateTimeOffset(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);
        foreach (int index in new[] { 3, 5, 7 })
        {
            var assessment = new Assessment { OwnerTeacherUserId = teacher.Id,
                Title = cases[index], AssessmentType = AssessmentType.PlacementTest,
                DurationMinutes = 30, MaxAttempts = 3,
                Status = index == 3 ? AssessmentStatus.Published : AssessmentStatus.Draft,
                Questions = new List<AssessmentQuestion> { new() { Question = questions[index] } } };
            db.Assessments.Add(assessment);
            if (index == 5)
                db.Attempts.Add(new Attempt { Assessment = assessment, StudentUserId = student.Id,
                    StartedAtUtc = started, DeadlineUtc = started.AddMinutes(30) });
        }
        var module = new Module { Title = "Module", Course = new Course {
            OwnerTeacherUserId = teacher.Id, Title = "Legacy course" } };
        foreach (int index in new[] { 4, 6, 8 })
        {
            var practice = new Practice { OwnerTeacherUserId = teacher.Id, Title = cases[index],
                Module = module, Status = index == 4 ? PracticeStatus.Unpublished : PracticeStatus.Draft,
                Questions = new List<PracticeQuestion> { new() { Question = questions[index] } } };
            db.Practices.Add(practice);
            if (index == 6)
                db.PracticeSubmissions.Add(new PracticeSubmission { Practice = practice,
                    StudentUserId = student.Id, Score = 60m, SubmittedAtUtc = started });
        }
        await db.SaveChangesAsync();

        // Chỉ trên database test: bỏ hai cột mới để tái hiện schema cũ có dữ liệu.
        // Sau đó chạy migration thật, không sao chép SQL backfill sang test.
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await migrator.MigrateAsync(LockMigration);
        db.ChangeTracker.Clear();

        var saved = await db.Questions.AsNoTracking().ToDictionaryAsync(q => q.Id);
        Assert.Equal(cases.Length, saved.Count);
        var lockTime = saved[questions[1].Id].ContentLockedAtUtc;
        Assert.NotNull(lockTime);
        Assert.Equal(TimeSpan.Zero, lockTime.Value.Offset);
        for (int index = 0; index < questions.Count; index++)
        {
            var actual = saved[questions[index].Id];
            if (index >= 1 && index <= 6)
                Assert.Equal(lockTime, actual.ContentLockedAtUtc);
            else
                Assert.Null(actual.ContentLockedAtUtc);
            Assert.Equal(questions[index].Content, actual.Content);
            Assert.Equal(questions[index].Status, actual.Status);
            Assert.Equal(questions[index].PrimarySkill, actual.PrimarySkill);
        }
        Assert.Equal(lockTime, (await db.Stimuli.FindAsync(shared.Id))!.ContentLockedAtUtc);
        Assert.Null((await db.Stimuli.FindAsync(draftOnly.Id))!.ContentLockedAtUtc);
        Assert.Equal("Shared passage", (await db.Stimuli.FindAsync(shared.Id))!.ContentText);
        Assert.Equal(cases.Length, await db.QuestionOptions.CountAsync(o => o.IsCorrect));
        Assert.Equal(cases.Length, await db.QuestionOptions.CountAsync(o => !o.IsCorrect));
        // Schema đang ở migration lịch sử, chưa có cột Version của Practice.
        var submission = await db.PracticeSubmissions.Select(s => new { s.Score, s.SubmittedAtUtc }).SingleAsync();
        Assert.Equal(60m, submission.Score);
        Assert.Equal(started, submission.SubmittedAtUtc);
        var attempt = await db.Attempts.SingleAsync();
        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
        Assert.Equal(started.AddMinutes(30), attempt.DeadlineUtc);
        Assert.Null(attempt.OverallScore);
    }

    // Kiểm tra cột nullable và private setter hoạt động khi lưu/đọc bằng EF thật.
    [Fact]
    public async Task EfRoundTrip_KeepsNewContentUnlockedAndPersistsFirstLockInUtc()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var teacher = await AddUserAsync(db, AppRoles.Teacher);
        var question = NewQuestion(teacher.Id, "New question");
        question.Stimulus = new Stimulus { OwnerTeacherUserId = teacher.Id,
            StimulusType = StimulusType.Passage, ContentText = "New passage" };
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        question = await db.Questions.Include(q => q.Stimulus).SingleAsync();
        Assert.Null(question.ContentLockedAtUtc);
        Assert.Null(question.Stimulus!.ContentLockedAtUtc);
        var localTime = new DateTimeOffset(2026, 10, 16, 9, 30, 0, TimeSpan.FromHours(7));
        question.LockContent(localTime);
        question.Stimulus.LockContent(localTime);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        question = await db.Questions.Include(q => q.Stimulus).SingleAsync();
        question.LockContent(localTime.AddDays(1));
        question.Stimulus!.LockContent(localTime.AddDays(1));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        question = await db.Questions.Include(q => q.Stimulus).SingleAsync();
        Assert.Equal(localTime.ToUniversalTime(), question.ContentLockedAtUtc);
        Assert.Equal(localTime.ToUniversalTime(), question.Stimulus!.ContentLockedAtUtc);
        Assert.Equal(TimeSpan.Zero, question.ContentLockedAtUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, question.Stimulus.ContentLockedAtUtc!.Value.Offset);
        Assert.Equal("New question", question.Content);
        Assert.Equal("New passage", question.Stimulus.ContentText);
    }

    // Chỉ tạo user và role cần cho khóa ngoại; không cần đăng nhập HTTP trong test migration.
    private static async Task<ApplicationUser> AddUserAsync(AppDbContext db, string role)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), FullName = role,
            UserName = Guid.NewGuid() + "@example.test" };
        db.Users.Add(user);
        var roleId = await db.Roles.Where(r => r.Name == role).Select(r => r.Id).SingleAsync();
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = roleId });
        return user;
    }

    private static Question NewQuestion(Guid teacherId, string content) => new()
    {
        OwnerTeacherUserId = teacherId, Content = content, PrimarySkill = EnglishSkill.Reading,
        Options = new List<QuestionOption>
        {
            new() { Content = "Correct", IsCorrect = true, OrderIndex = 0 },
            new() { Content = "Incorrect", IsCorrect = false, OrderIndex = 1 }
        }
    };
}
