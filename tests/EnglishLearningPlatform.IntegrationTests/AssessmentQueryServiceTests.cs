using System.Data.Common;
using System.Text.Json;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.AssessmentAttempts;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class AssessmentQueryServiceTests(IntegrationTestFactory factory)
    : IClassFixture<IntegrationTestFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    private async Task<Fixture> SeedAsync(bool courseLinked = false, bool paid = false)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Course? course = courseLinked ? new Course
        {
            OwnerTeacherUserId = teacher.Id, Title = "Query course", Status = CourseStatus.Published,
            IsPaid = paid, Price = paid ? 100m : 0m
        } : null;
        Payment? payment = paid ? new Payment
        {
            Amount = 100m, Status = PaymentStatus.Succeeded, Provider = "Test",
            ProviderTransactionId = Guid.NewGuid().ToString("N"),
            Order = new Order { StudentUserId = student.Id, Course = course!, Amount = 100m, Currency = "VND", Provider = "Test" }
        } : null;
        if (course is not null)
            db.Enrollments.Add(new Enrollment { StudentUserId = student.Id, Course = course, Payment = payment });

        var assessment = new Assessment
        {
            OwnerTeacherUserId = teacher.Id, Course = course, Title = "Query assessment", Description = "Safe description",
            AssessmentType = AssessmentType.PracticeExam, Status = AssessmentStatus.Published,
            DurationMinutes = 15, MaxAttempts = 10, PassingScore = 80m,
            Questions = Enumerable.Range(0, 5).Reverse().Select(index => new AssessmentQuestion
            {
                OrderIndex = index, Points = index == 0 ? 2m : 1m,
                Question = new Question
                {
                    OwnerTeacherUserId = teacher.Id, Content = $"Question {index}",
                    Explanation = "Private explanation sentinel", Status = QuestionStatus.Published,
                    PrimarySkill = index == 0 ? EnglishSkill.Vocabulary : EnglishSkill.Grammar,
                    Stimulus = index == 0 ? new Stimulus
                    {
                        OwnerTeacherUserId = teacher.Id, StimulusType = StimulusType.Passage, ContentText = "Shared passage"
                    } : null,
                    Options =
                    [
                        new QuestionOption { OrderIndex = 2, Content = "Option C" },
                        new QuestionOption { OrderIndex = 0, Content = "Option A", IsCorrect = true },
                        new QuestionOption { OrderIndex = 1, Content = "Option B" }
                    ]
                }
            }).ToList()
        };
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();
        return new Fixture(student.Id, teacher.Id, assessment, course, payment);
    }

    private static Attempt AttemptFor(Fixture data, AttemptStatus status = AttemptStatus.InProgress, decimal? score = null) => new()
    {
        StudentUserId = data.Student, AssessmentId = data.Assessment.Id, Status = status,
        StartedAtUtc = Now.AddMinutes(-1), DeadlineUtc = Now.AddMinutes(14), OverallScore = score,
        Passed = score.HasValue ? score >= 80m : null,
        FinalizedAtUtc = status == AttemptStatus.Graded ? Now.AddSeconds(-30) : null,
        FinalizationReason = status == AttemptStatus.Graded ? AttemptFinalizationReason.ManualSubmit : null
    };

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/private.mp3")]
    [InlineData("/relative-audio.mp3")]
    public async Task TakingSheet_DoesNotExposeUnsafeAudioUrls(string audioUrl)
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stimulus = await db.Stimuli.SingleAsync(s => s.OwnerTeacherUserId == data.Teacher);
        stimulus.StimulusType = StimulusType.Audio;
        stimulus.ResourceUrl = audioUrl;
        var attempt = AttemptFor(data);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        var query = new AssessmentQueryService(db, new FixedTimeProvider(Now));
        var sheet = await query.GetTakingSheetAsync(data.Student, attempt.Id);
        Assert.NotNull(sheet);
        Assert.All(sheet.Questions, question => Assert.Null(question.AudioUrl));
        Assert.Contains(sheet.Questions, question => question.StimulusContent == "Shared passage");
    }

    [Fact]
    public async Task Overview_UsesOnlyOwnedGradedScoresAndCountsExpiredAttemptsWithoutMutating()
    {
        var data = await SeedAsync();
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var active = AttemptFor(data);
        var foreign = AttemptFor(data, AttemptStatus.Graded, 100m);
        foreign.StudentUserId = other.Id;
        db.Attempts.AddRange(active, AttemptFor(data, AttemptStatus.Graded, 40m),
            AttemptFor(data, AttemptStatus.Graded, 90m), AttemptFor(data, AttemptStatus.Submitted, 100m), foreign);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var query = new AssessmentQueryService(db, new FixedTimeProvider(Now));

        var overview = await query.GetAssessmentOverviewAsync(data.Student, data.Assessment.Id);

        Assert.NotNull(overview);
        Assert.Equal(4, overview.UsedAttempts);
        Assert.Equal(90m, overview.HighestScore);
        Assert.Equal(active.Id, overview.ActiveAttemptId);
        Assert.Equal(active.DeadlineUtc, overview.ActiveDeadlineUtc);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        await db.Attempts.Where(a => a.Id == active.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.DeadlineUtc, Now));
        overview = await query.GetAssessmentOverviewAsync(data.Student, data.Assessment.Id);
        Assert.NotNull(overview);
        Assert.Null(overview.ActiveAttemptId);
        Assert.Null(overview.ActiveDeadlineUtc);
        Assert.Equal(4, overview.UsedAttempts);
        Assert.Equal(AttemptStatus.InProgress, await db.Attempts.Where(a => a.Id == active.Id).Select(a => a.Status).SingleAsync());
    }

    [Fact]
    public async Task DependencyInjection_ResolvesQueryAndEmptyHistoryHasNoHighestScore()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IAssessmentQueryService>();
        Assert.IsType<AssessmentQueryService>(query);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TimeProvider>());
        var overview = await query.GetAssessmentOverviewAsync(data.Student, data.Assessment.Id);
        Assert.NotNull(overview);
        Assert.Equal(0, overview.UsedAttempts);
        Assert.Null(overview.HighestScore);
        Assert.Null(overview.ActiveAttemptId);
        Assert.Null(await query.GetAssessmentOverviewAsync(Guid.NewGuid(), data.Assessment.Id));
        Assert.Null(await query.GetAssessmentOverviewAsync(data.Student, Guid.NewGuid()));
    }

    [Fact]
    public async Task TakingSheet_OrdersQuestionsAndOptionsIncludesSavedChoiceAndNeverReadsAnswerKey()
    {
        var data = await SeedAsync(courseLinked: true);
        using var scope = factory.Services.CreateScope();
        var seedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempt = AttemptFor(data);
        var first = data.Assessment.Questions.Single(q => q.OrderIndex == 0);
        var savedOption = first.Question.Options.Single(o => o.OrderIndex == 1).Id;
        attempt.Answers.Add(new AttemptAnswer
        {
            AssessmentQuestionId = first.Id, SelectedOptionId = savedOption, SavedAtUtc = Now.AddSeconds(-10)
        });
        seedDb.Attempts.Add(attempt);
        await seedDb.SaveChangesAsync();
        var collector = new ReadCommandCollector();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(seedDb.Database.GetConnectionString()).AddInterceptors(collector).Options;
        await using var db = new AppDbContext(options);
        var query = new AssessmentQueryService(db, new FixedTimeProvider(Now));

        var sheet = await query.GetTakingSheetAsync(data.Student, attempt.Id);

        Assert.NotNull(sheet);
        Assert.Equal(attempt.DeadlineUtc, sheet.DeadlineUtc);
        Assert.Equal(Enumerable.Range(0, 5).Select(index => $"Question {index}"), sheet.Questions.Select(q => q.Content));
        Assert.All(sheet.Questions, q => Assert.Equal(new[] { 0, 1, 2 }, q.Options.Select(o => o.OrderIndex)));
        Assert.Equal(savedOption, sheet.Questions[0].SelectedOptionId);
        Assert.Equal("Shared passage", sheet.Questions[0].StimulusContent);
        Assert.All(sheet.Questions.Skip(1), q => Assert.Null(q.SelectedOptionId));
        var json = JsonSerializer.Serialize(sheet);
        Assert.DoesNotContain("IsCorrect", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Explanation", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Private explanation sentinel", json);
        Assert.NotEmpty(collector.Commands);
        Assert.All(collector.Commands, sql =>
        {
            Assert.DoesNotContain("[IsCorrect]", sql);
            Assert.DoesNotContain("[Explanation]", sql);
        });
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        Assert.Null(await query.GetTakingSheetAsync(other.Id, attempt.Id));
        Assert.Null(await query.GetTakingSheetAsync(data.Student, Guid.NewGuid()));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task TakingSheet_EnforcesServerDeadlineWithoutFinalizing(int secondsRemaining, bool available)
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempt = AttemptFor(data);
        attempt.DeadlineUtc = Now.AddSeconds(secondsRemaining);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var query = new AssessmentQueryService(db, new FixedTimeProvider(Now));

        Assert.Equal(available, await query.GetTakingSheetAsync(data.Student, attempt.Id) is not null);
        Assert.Null(await query.GetAttemptResultAsync(data.Student, attempt.Id));
        var stored = await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(AttemptStatus.InProgress, stored.Status);
        Assert.Null(stored.FinalizedAtUtc);
        Assert.Null(stored.OverallScore);
    }

    [Fact]
    public async Task TakingSheet_DoesNotReturnDeadlineThatElapsedDuringRead()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempt = AttemptFor(data);
        attempt.DeadlineUtc = Now.AddSeconds(1);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        var query = new AssessmentQueryService(db, new AdvancingTimeProvider());
        Assert.Null(await query.GetTakingSheetAsync(data.Student, attempt.Id));
    }

    [Theory]
    [InlineData(AttemptStatus.Submitted)]
    [InlineData(AttemptStatus.AutoSubmitted)]
    [InlineData(AttemptStatus.Graded)]
    public async Task TakingSheet_HidesSubmittedAndGradedAttempts(AttemptStatus status)
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempt = AttemptFor(data, status, 90m);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        Assert.Null(await new AssessmentQueryService(db, new FixedTimeProvider(Now)).GetTakingSheetAsync(data.Student, attempt.Id));
    }

    [Theory]
    [InlineData("NoEnrollment")]
    [InlineData("FailedPayment")]
    [InlineData("WrongStudent")]
    [InlineData("WrongCourse")]
    [InlineData("AmountMismatch")]
    [InlineData("CurrencyMismatch")]
    [InlineData("OwnerMismatch")]
    public async Task OverviewAndTakingSheet_RejectUnverifiedEnrollmentAndMismatchedCourseOwnership(string corruption)
    {
        var data = await SeedAsync(courseLinked: true, paid: true);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempt = AttemptFor(data);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        switch (corruption)
        {
            case "NoEnrollment":
                await db.Enrollments.Where(e => e.StudentUserId == data.Student).ExecuteDeleteAsync();
                break;
            case "FailedPayment":
                await db.Payments.Where(p => p.Id == data.Payment!.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Failed));
                break;
            case "WrongStudent":
                var otherStudent = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
                await db.Orders.Where(o => o.Id == data.Payment!.OrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.StudentUserId, otherStudent.Id));
                break;
            case "WrongCourse":
                var course = new Course { OwnerTeacherUserId = data.Teacher, Title = "Other course", Status = CourseStatus.Published };
                db.Courses.Add(course);
                await db.SaveChangesAsync();
                await db.Orders.Where(o => o.Id == data.Payment!.OrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.CourseId, course.Id));
                break;
            case "AmountMismatch":
                await db.Payments.Where(p => p.Id == data.Payment!.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Amount, 99m));
                break;
            case "CurrencyMismatch":
                await db.Orders.Where(o => o.Id == data.Payment!.OrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.Currency, "USD"));
                break;
            case "OwnerMismatch":
                var otherTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
                await db.Assessments.Where(a => a.Id == data.Assessment.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.OwnerTeacherUserId, otherTeacher.Id));
                break;
        }
        db.ChangeTracker.Clear();
        var query = new AssessmentQueryService(db, new FixedTimeProvider(Now));
        Assert.Null(await query.GetAssessmentOverviewAsync(data.Student, data.Assessment.Id));
        Assert.Null(await query.GetTakingSheetAsync(data.Student, attempt.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverviewAndTakingSheet_AllowValidFreeAndPaidEnrollment(bool paid)
    {
        var data = await SeedAsync(courseLinked: true, paid: paid);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempt = AttemptFor(data);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        var query = new AssessmentQueryService(db, new FixedTimeProvider(Now));
        Assert.NotNull(await query.GetAssessmentOverviewAsync(data.Student, data.Assessment.Id));
        Assert.NotNull(await query.GetTakingSheetAsync(data.Student, attempt.Id));
        if (!paid)
        {
            // A genuine free enrollment remains valid after a pricing change.
            await db.Courses.Where(c => c.Id == data.Course!.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsPaid, true).SetProperty(c => c.Price, 100m));
            Assert.NotNull(await query.GetAssessmentOverviewAsync(data.Student, data.Assessment.Id));
            Assert.NotNull(await query.GetTakingSheetAsync(data.Student, attempt.Id));
        }
    }

    [Theory]
    [InlineData(AccountStatus.Locked)]
    [InlineData(AccountStatus.Disabled)]
    public async Task AllQueries_RequireActiveStudent(AccountStatus status)
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var active = AttemptFor(data);
        var graded = AttemptFor(data, AttemptStatus.Graded, 90m);
        db.Attempts.AddRange(active, graded);
        await db.SaveChangesAsync();
        await db.Users.Where(u => u.Id == data.Student).ExecuteUpdateAsync(s => s.SetProperty(u => u.AccountStatus, status));
        var query = new AssessmentQueryService(db, new FixedTimeProvider(Now));
        Assert.Null(await query.GetAssessmentOverviewAsync(data.Student, data.Assessment.Id));
        Assert.Null(await query.GetTakingSheetAsync(data.Student, active.Id));
        Assert.Null(await query.GetAttemptResultAsync(data.Student, graded.Id));
    }

    [Theory]
    [InlineData(AppRoles.Teacher)]
    [InlineData(AppRoles.Admin)]
    [InlineData(null)]
    public async Task AllQueries_RequireExactlyOneStudentRole(string? replacementRole)
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var active = AttemptFor(data);
        var graded = AttemptFor(data, AttemptStatus.Graded, 90m);
        db.Attempts.AddRange(active, graded);
        await db.SaveChangesAsync();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var student = await manager.FindByIdAsync(data.Student.ToString());
        Assert.NotNull(student);
        Assert.True((await manager.RemoveFromRoleAsync(student, AppRoles.Student)).Succeeded);
        if (replacementRole is not null)
            Assert.True((await manager.AddToRoleAsync(student, replacementRole)).Succeeded);
        var query = new AssessmentQueryService(db, new FixedTimeProvider(Now));
        Assert.Null(await query.GetAssessmentOverviewAsync(data.Student, data.Assessment.Id));
        Assert.Null(await query.GetTakingSheetAsync(data.Student, active.Id));
        Assert.Null(await query.GetAttemptResultAsync(data.Student, graded.Id));
    }

    [Theory]
    [InlineData(AssessmentStatus.Unpublished)]
    [InlineData(AssessmentStatus.Archived)]
    public async Task WithdrawnAssessment_HidesOverviewButPreservesExistingTakingSheet(AssessmentStatus status)
    {
        var data = await SeedAsync(courseLinked: true);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempt = AttemptFor(data);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        await db.Assessments.Where(a => a.Id == data.Assessment.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status));
        await db.Courses.Where(c => c.Id == data.Course!.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CourseStatus.Archived));
        var query = new AssessmentQueryService(db, new FixedTimeProvider(Now));
        Assert.Null(await query.GetAssessmentOverviewAsync(data.Student, data.Assessment.Id));
        var sheet = await query.GetTakingSheetAsync(data.Student, attempt.Id);
        Assert.NotNull(sheet);
        Assert.Equal(attempt.DeadlineUtc, sheet.DeadlineUtc);
    }

    [Theory]
    [InlineData(CourseStatus.Draft)]
    [InlineData(CourseStatus.Archived)]
    public async Task Overview_UnavailableCourseOnlyPermitsExistingActiveAttempt(CourseStatus status)
    {
        var data = await SeedAsync(courseLinked: true);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Courses.Where(c => c.Id == data.Course!.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, status));
        var query = new AssessmentQueryService(db, new FixedTimeProvider(Now));
        Assert.Null(await query.GetAssessmentOverviewAsync(data.Student, data.Assessment.Id));
        var attempt = AttemptFor(data);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        Assert.Equal(attempt.Id, (await query.GetAssessmentOverviewAsync(data.Student, data.Assessment.Id))!.ActiveAttemptId);
        Assert.NotNull(await query.GetTakingSheetAsync(data.Student, attempt.Id));
    }

    [Fact]
    public async Task Overview_DisabledIndependentTeacherOnlyPermitsExistingActiveAttempt()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users.Where(u => u.Id == data.Teacher).ExecuteUpdateAsync(s => s.SetProperty(u => u.AccountStatus, AccountStatus.Disabled));
        var query = new AssessmentQueryService(db, new FixedTimeProvider(Now));
        Assert.Null(await query.GetAssessmentOverviewAsync(data.Student, data.Assessment.Id));
        var attempt = AttemptFor(data);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        Assert.Equal(attempt.Id, (await query.GetAssessmentOverviewAsync(data.Student, data.Assessment.Id))!.ActiveAttemptId);
        Assert.NotNull(await query.GetTakingSheetAsync(data.Student, attempt.Id));
    }

    [Fact]
    public async Task Result_PreservesOwnersHistoricalGradeAfterWithdrawalAndEnrollmentRevocation()
    {
        var data = await SeedAsync(courseLinked: true, paid: true);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempt = AttemptFor(data, AttemptStatus.Graded, 88m);
        attempt.SkillResults =
        [
            new AttemptSkillResult { Skill = EnglishSkill.Grammar, Score = 75m, QuestionCount = 4 },
            new AttemptSkillResult { Skill = EnglishSkill.Vocabulary, Score = 100m, QuestionCount = 1 }
        ];
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        await db.Assessments.Where(a => a.Id == data.Assessment.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AssessmentStatus.Archived));
        await db.Payments.Where(p => p.Id == data.Payment!.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Failed));
        await db.Enrollments.Where(e => e.StudentUserId == data.Student).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
        var query = new AssessmentQueryService(db, new FixedTimeProvider(Now.AddYears(1)));

        var result = await query.GetAttemptResultAsync(data.Student, attempt.Id);

        Assert.NotNull(result);
        Assert.Equal(88m, result.OverallScore);
        Assert.True(result.Passed);
        Assert.Equal(attempt.FinalizedAtUtc, result.FinalizedAtUtc);
        Assert.Equal(AttemptFinalizationReason.ManualSubmit, result.FinalizationReason);
        Assert.Equal(new[] { EnglishSkill.Vocabulary, EnglishSkill.Grammar }, result.SkillBreakdowns.Select(s => s.Skill));
        Assert.Null(await query.GetAttemptResultAsync(other.Id, attempt.Id));
        Assert.Null(await query.GetAttemptResultAsync(data.Student, Guid.NewGuid()));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("Score")]
    [InlineData("FinalizedAtUtc")]
    [InlineData("FinalizationReason")]
    [InlineData("Status")]
    public async Task Result_DoesNotInventMissingFinalizationMetadata(string missing)
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempt = AttemptFor(data, AttemptStatus.Graded, 90m);
        switch (missing)
        {
            case "Score": attempt.OverallScore = null; break;
            case "FinalizedAtUtc": attempt.FinalizedAtUtc = null; break;
            case "FinalizationReason": attempt.FinalizationReason = null; break;
            case "Status": attempt.Status = AttemptStatus.Submitted; break;
        }
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        var query = new AssessmentQueryService(db, new FixedTimeProvider(Now));
        Assert.Null(await query.GetAttemptResultAsync(data.Student, attempt.Id));
    }

    [Fact]
    public async Task FinalizedAttempt_ResultReadsPersistedWeightedSkillBreakdown()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAttemptService>();
        var started = await service.StartAsync(data.Student, data.Assessment.Id);
        Assert.Equal(AttemptStartStatus.Started, started.Status);
        foreach (var question in data.Assessment.Questions)
        {
            var option = question.Question.Options.Single(o => o.OrderIndex == (question.OrderIndex == 4 ? 1 : 0));
            Assert.Equal(AttemptSaveStatus.Saved, await service.SaveAnswerAsync(data.Student, started.AttemptId!.Value, question.Id, option.Id));
        }
        var finalized = await service.FinalizeAsync(data.Student, started.AttemptId!.Value);
        Assert.Equal(AttemptFinalizeStatus.Graded, finalized.Status);
        var query = scope.ServiceProvider.GetRequiredService<IAssessmentQueryService>();
        var result = await query.GetAttemptResultAsync(data.Student, started.AttemptId.Value);
        Assert.NotNull(result);
        Assert.Equal(83.33m, result.OverallScore);
        Assert.True(result.Passed);
        Assert.Collection(result.SkillBreakdowns,
            vocabulary => { Assert.Equal(EnglishSkill.Vocabulary, vocabulary.Skill); Assert.Equal(100m, vocabulary.Score); Assert.Equal(1, vocabulary.QuestionCount); },
            grammar => { Assert.Equal(EnglishSkill.Grammar, grammar.Skill); Assert.Equal(75m, grammar.Score); Assert.Equal(4, grammar.QuestionCount); });
        var finalizedAt = result.FinalizedAtUtc;
        Assert.Equal(AttemptFinalizeStatus.AlreadyGraded, (await service.FinalizeAsync(data.Student, started.AttemptId.Value)).Status);
        Assert.Equal(finalizedAt, (await query.GetAttemptResultAsync(data.Student, started.AttemptId.Value))!.FinalizedAtUtc);
    }

    private sealed record Fixture(Guid Student, Guid Teacher, Assessment Assessment, Course? Course, Payment? Payment);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AdvancingTimeProvider : TimeProvider
    {
        private int reads;
        public override DateTimeOffset GetUtcNow() => reads++ == 0 ? Now : Now.AddSeconds(1);
    }

    private sealed class ReadCommandCollector : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
