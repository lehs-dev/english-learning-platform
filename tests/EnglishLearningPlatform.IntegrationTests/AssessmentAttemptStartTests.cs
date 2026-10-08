
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class AssessmentAttemptStartTests(
    IntegrationTestFactory factory)
    : IClassFixture<IntegrationTestFactory>
{
    private async Task<(Guid StudentId, Guid AssessmentId)> SeedAsync()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(
            factory, AppRoles.Teacher);

        var student = await IdentityTestHelpers.CreateUserAsync(
            factory, AppRoles.Student);

        var assessment = new Assessment
        {
            OwnerTeacherUserId = teacher.Id,
            Title = "Concurrency assessment",
            AssessmentType = AssessmentType.SkillAssessment,
            TargetSkill = EnglishSkill.Grammar,
            Status = AssessmentStatus.Published,
            DurationMinutes = 20,
            MaxAttempts = 1,
            PassingScore = 70,

            Questions = Enumerable.Range(0, 5)
                .Select(i => new AssessmentQuestion
                {
                    OrderIndex = i,
                    Points = 1m,
                    Question = new Question
                    {
                        OwnerTeacherUserId = teacher.Id,
                        Content = $"Question {i}",
                        PrimarySkill = EnglishSkill.Grammar,
                        Status = QuestionStatus.Published,
                        Options =
                        [
                            new QuestionOption
                            {
                                OrderIndex = 0,
                                Content = "Correct",
                                IsCorrect = true
                            },
                            new QuestionOption
                            {
                                OrderIndex = 1,
                                Content = "Wrong",
                                IsCorrect = false
                            }
                        ]
                    }
                })
                .ToList()
        };

        using var scope = factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();

        return (student.Id, assessment.Id);
    }

    private async Task<AttemptStartResult> StartAsync(
        Guid studentId, Guid assessmentId)
    {
        // Mỗi request dùng DI scope và DbContext riêng.
        using var scope = factory.Services.CreateScope();

        var service = scope.ServiceProvider
            .GetRequiredService<IAttemptService>();

        return await service.StartAsync(studentId, assessmentId);
    }

    [Fact]
    public async Task ConcurrentStarts_ReuseOneActiveAttempt()
    {
        var (studentId, assessmentId) = await SeedAsync();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 5)
                .Select(_ => StartAsync(studentId, assessmentId)));

        Assert.Single(
            results,
            (r => r.Status == AttemptStartStatus.Started));

        Assert.All(results, r =>
        {
            Assert.True(
                r.Status is AttemptStartStatus.Started
                    or AttemptStartStatus.Resumed);

            Assert.NotNull(r.AttemptId);
            Assert.NotNull(r.DeadlineUtc);
        });

        var attemptId = results[0].AttemptId;
        var deadline = results[0].DeadlineUtc;

        Assert.All(results, r =>
        {
            Assert.Equal(attemptId, r.AttemptId);
            Assert.Equal(deadline, r.DeadlineUtc);
        });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var attempts = await db.Attempts
            .Where(a =>
                a.StudentUserId == studentId &&
                a.AssessmentId == assessmentId)
            .ToListAsync();

        var saved = Assert.Single(attempts);

        Assert.Equal(attemptId, saved.Id);
        Assert.Equal(AttemptStatus.InProgress, saved.Status);
        Assert.Equal(deadline, saved.DeadlineUtc);
    }

    [Theory]
    [InlineData(false, AttemptStartStatus.Resumed)]
    [InlineData(true, AttemptStartStatus.NeedsFinalization)]
    public async Task ActiveAttempt_AtMaxAttempts_ReturnsExistingAttempt(
        bool expired, AttemptStartStatus expectedStatus)
    {
        var (studentId, assessmentId) = await SeedAsync();
        var first = await StartAsync(studentId, assessmentId);

        Assert.Equal(AttemptStartStatus.Started, first.Status);
        Assert.NotNull(first.AttemptId);
        Assert.NotNull(first.DeadlineUtc);

        var deadline = first.DeadlineUtc.Value;

        if (expired)
        {
            deadline = DateTimeOffset.UtcNow.AddMinutes(-1);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await db.Attempts
                .Where(a => a.Id == first.AttemptId.Value)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(a => a.DeadlineUtc, deadline));
        }

        var second = await StartAsync(studentId, assessmentId);

        Assert.Equal(expectedStatus, second.Status);
        Assert.Equal(first.AttemptId, second.AttemptId);
        Assert.Equal(deadline, second.DeadlineUtc);

        using var checkScope = factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = Assert.Single(await checkDb.Attempts
            .Where(a => a.StudentUserId == studentId &&
                        a.AssessmentId == assessmentId)
            .ToListAsync());

        Assert.Equal(first.AttemptId, saved.Id);
        Assert.Equal(AttemptStatus.InProgress, saved.Status);
        Assert.Equal(deadline, saved.DeadlineUtc);
    }

    [Fact]
    public async Task FinalizedAttempt_ConsumesMaxAttempts()
    {
        var (studentId, assessmentId) = await SeedAsync();

        var first = await StartAsync(studentId, assessmentId);

        Assert.Equal(AttemptStartStatus.Started, first.Status);
        Assert.NotNull(first.AttemptId);

        // Giả lập một Attempt đã chấm.
        // FinalizeService thật sẽ được viết ở bước sau.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            await db.Attempts
                .Where(a => a.Id == first.AttemptId.Value)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        a => a.Status,
                        AttemptStatus.Graded)
                    .SetProperty(
                        a => a.OverallScore,
                        (decimal?)100m)
                    .SetProperty(
                        a => a.Passed,
                        (bool?)true)
                    .SetProperty(
                        a => a.FinalizedAtUtc,
                        (DateTimeOffset?)DateTimeOffset.UtcNow)
                    .SetProperty(
                        a => a.FinalizationReason,
                        (AttemptFinalizationReason?)
                            AttemptFinalizationReason.ManualSubmit));
        }

        var second = await StartAsync(studentId, assessmentId);

        Assert.Equal(
            AttemptStartStatus.LimitReached,
            second.Status);

        using var checkScope = factory.Services.CreateScope();

        var checkDb = checkScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        Assert.Equal(1, await checkDb.Attempts.CountAsync(
            a => a.StudentUserId == studentId &&
                 a.AssessmentId == assessmentId));
    }


    [Fact]
    public async Task ConcurrentFinalization_GradesOnlyOnce()
    {
        var (studentId, assessmentId) = await SeedAsync();

        var started = await StartAsync(studentId, assessmentId);

        Assert.Equal(AttemptStartStatus.Started, started.Status);
        Assert.NotNull(started.AttemptId);

        var attemptId = started.AttemptId.Value;

        // Lưu một đáp án đúng trong tổng số 5 câu.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var question = await db.AssessmentQuestions
                .AsNoTracking()
                .Where(q => q.AssessmentId == assessmentId)
                .OrderBy(q => q.OrderIndex)
                .Select(q => new
                {
                    q.Id,
                    q.QuestionId
                })
                .FirstAsync();

            var correctOptionId = await db.QuestionOptions
                .AsNoTracking()
                .Where(o => o.QuestionId == question.QuestionId &&
                            o.IsCorrect)
                .Select(o => o.Id)
                .SingleAsync();

            db.AttemptAnswers.Add(new AttemptAnswer
            {
                AttemptId = attemptId,
                AssessmentQuestionId = question.Id,
                SelectedOptionId = correctOptionId
            });

            await db.SaveChangesAsync();
        }

        async Task<AttemptFinalizeResult> FinalizeOnceAsync()
        {
            using var scope = factory.Services.CreateScope();

            var service = scope.ServiceProvider
                .GetRequiredService<IAttemptService>();

            return await service.FinalizeAsync(studentId, attemptId);
        }

        var results = await Task.WhenAll(
            Enumerable.Range(0, 5)
                .Select(_ => FinalizeOnceAsync()));

        Assert.Single(results,
            r => r.Status == AttemptFinalizeStatus.Graded);

        Assert.Equal(4, results.Count(r =>
            r.Status == AttemptFinalizeStatus.AlreadyGraded));

        Assert.All(results, r =>
        {
            Assert.Equal(attemptId, r.AttemptId);
            Assert.Equal(20m, r.OverallScore);
            Assert.Equal(false, r.Passed);
            Assert.Equal(
                AttemptFinalizationReason.ManualSubmit,
                r.Reason);
        });

        using var finalScope = factory.Services.CreateScope();

        var finalDb = finalScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var saved = await finalDb.Attempts
            .AsNoTracking()
            .SingleAsync(a => a.Id == attemptId);

        Assert.Equal(AttemptStatus.Graded, saved.Status);
        Assert.Equal(20m, saved.OverallScore);
        Assert.NotNull(saved.FinalizedAtUtc);
    }

}
