
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
    [InlineData(true, AttemptStartStatus.ExpiredFinalized)]
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
        Assert.Equal(
            expired ? AttemptStatus.Graded : AttemptStatus.InProgress,
            saved.Status);
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


    [Fact]
    public async Task SaveAnswer_ValidatesOwnershipOptionAndFinalization()
    {
        var (studentId, assessmentId) = await SeedAsync();
        var started = await StartAsync(studentId, assessmentId);

        Assert.Equal(AttemptStartStatus.Started, started.Status);

        var attemptId = started.AttemptId!.Value;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var service = scope.ServiceProvider
            .GetRequiredService<IAttemptService>();

        var questions = await db.AssessmentQuestions
            .AsNoTracking()
            .Where(q => q.AssessmentId == assessmentId)
            .OrderBy(q => q.OrderIndex)
            .Select(q => new { q.Id, q.QuestionId })
            .Take(2)
            .ToListAsync();

        Assert.Equal(2, questions.Count);

        var first = questions[0];
        var second = questions[1];

        var correctOptionId = await db.QuestionOptions
            .Where(o => o.QuestionId == first.QuestionId &&
                        o.IsCorrect)
            .Select(o => o.Id)
            .SingleAsync();

        var foreignOptionId = await db.QuestionOptions
            .Where(o => o.QuestionId == second.QuestionId)
            .Select(o => o.Id)
            .FirstAsync();

        // Option thuộc câu khác phải bị từ chối.
        Assert.Equal(
            AttemptSaveStatus.InvalidAnswer,
            await service.SaveAnswerAsync(
                studentId, attemptId, first.Id, foreignOptionId));

        // Lưu cùng một câu nhiều lần không tạo Answer trùng.
        Assert.Equal(
            AttemptSaveStatus.Saved,
            await service.SaveAnswerAsync(
                studentId, attemptId, first.Id, correctOptionId));

        Assert.Equal(
            AttemptSaveStatus.Saved,
            await service.SaveAnswerAsync(
                studentId, attemptId, first.Id, correctOptionId));

        Assert.Equal(1, await db.AttemptAnswers.CountAsync(
            a => a.AttemptId == attemptId));

        // Học viên khác không được sửa Attempt này.
        var other = await IdentityTestHelpers.CreateUserAsync(
            factory, AppRoles.Student);

        Assert.Equal(
            AttemptSaveStatus.NotFound,
            await service.SaveAnswerAsync(
                other.Id, attemptId, first.Id, correctOptionId));

        // Nộp bài và chấm điểm.
        var graded = await service.FinalizeAsync(
            studentId, attemptId);

        Assert.Equal(AttemptFinalizeStatus.Graded, graded.Status);
        Assert.Equal(20m, graded.OverallScore);

        // Sau Finalize, không thể ghi lại đáp án.
        Assert.Equal(
            AttemptSaveStatus.AlreadyFinalized,
            await service.SaveAnswerAsync(
                studentId, attemptId, first.Id, foreignOptionId));

        Assert.Equal(1, await db.AttemptAnswers.CountAsync(
            a => a.AttemptId == attemptId));

        var saved = await db.AttemptAnswers.AsNoTracking()
            .SingleAsync(a => a.AttemptId == attemptId);

        Assert.Equal(correctOptionId, saved.SelectedOptionId);
    }


    [Fact]
    public async Task ConcurrentSaveAndFinalize_PreservesGradedResult()
    {
        var (studentId, assessmentId) = await SeedAsync();
        var started = await StartAsync(studentId, assessmentId);

        Assert.Equal(AttemptStartStatus.Started, started.Status);
        var attemptId = started.AttemptId!.Value;

        Guid assessmentQuestionId;
        Guid correctOptionId;
        Guid wrongOptionId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var question = await db.AssessmentQuestions
                .AsNoTracking()
                .Where(q => q.AssessmentId == assessmentId)
                .OrderBy(q => q.OrderIndex)
                .Select(q => new { q.Id, q.QuestionId })
                .FirstAsync();

            assessmentQuestionId = question.Id;

            var options = await db.QuestionOptions
                .AsNoTracking()
                .Where(o => o.QuestionId == question.QuestionId)
                .Select(o => new { o.Id, o.IsCorrect })
                .ToListAsync();

            correctOptionId = options.Single(o => o.IsCorrect).Id;
            wrongOptionId = options.Single(o => !o.IsCorrect).Id;
        }

        // Cho các request cùng bắt đầu ở thời điểm gần nhau.
        var gate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<AttemptSaveStatus> SaveOnceAsync(Guid optionId)
        {
            await gate.Task;

            using var scope = factory.Services.CreateScope();
            var service = scope.ServiceProvider
                .GetRequiredService<IAttemptService>();

            return await service.SaveAnswerAsync(
                studentId,
                attemptId,
                assessmentQuestionId,
                optionId);
        }

        async Task<AttemptFinalizeResult> FinalizeOnceAsync()
        {
            await gate.Task;

            using var scope = factory.Services.CreateScope();
            var service = scope.ServiceProvider
                .GetRequiredService<IAttemptService>();

            return await service.FinalizeAsync(studentId, attemptId);
        }

        // 10 request Save xen kẽ đáp án đúng và sai.
        var saveTasks = Enumerable.Range(0, 10)
            .Select(i => SaveOnceAsync(
                i % 2 == 0 ? correctOptionId : wrongOptionId))
            .ToArray();

        // 5 request Finalize đồng thời.
        var finalizeTasks = Enumerable.Range(0, 5)
            .Select(_ => FinalizeOnceAsync())
            .ToArray();

        gate.SetResult(true);

        await Task.WhenAll(
            saveTasks.Cast<Task>().Concat(finalizeTasks.Cast<Task>()));

        var saveResults = await Task.WhenAll(saveTasks);
        var finalizeResults = await Task.WhenAll(finalizeTasks);

        // Save chỉ có thể thành công trước Finalize
        // hoặc bị từ chối sau Finalize.
        Assert.All(saveResults, result =>
            Assert.True(
                result is AttemptSaveStatus.Saved
                    or AttemptSaveStatus.AlreadyFinalized,
                $"Unexpected Save status: {result}"));

        // Chỉ một request thực sự chấm điểm.
        Assert.Single(
            finalizeResults,
            r => r.Status == AttemptFinalizeStatus.Graded);

        Assert.Equal(
            4,
            finalizeResults.Count(r =>
                r.Status == AttemptFinalizeStatus.AlreadyGraded));

        using var finalScope = factory.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var persisted = await finalDb.Attempts
            .AsNoTracking()
            .SingleAsync(a => a.Id == attemptId);

        var answer = await finalDb.AttemptAnswers
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId);

        Assert.Equal(AttemptStatus.Graded, persisted.Status);
        Assert.NotNull(persisted.FinalizedAtUtc);

        // Trong đề 5 câu, mỗi câu 1 điểm:
        // đúng 1 câu = 20%, sai/chưa trả lời = 0%.
        var expectedScore =
            answer?.SelectedOptionId == correctOptionId
                ? 20m
                : 0m;

        Assert.Equal(expectedScore, persisted.OverallScore);

        Assert.All(finalizeResults, result =>
            Assert.Equal(persisted.OverallScore, result.OverallScore));

        // Sau khi tất cả request hoàn tất,
        // mọi thao tác Save mới đều phải bị từ chối.
        using var checkScope = factory.Services.CreateScope();
        var checkService = checkScope.ServiceProvider
            .GetRequiredService<IAttemptService>();

        var lateSave = await checkService.SaveAnswerAsync(
            studentId,
            attemptId,
            assessmentQuestionId,
            wrongOptionId);

        Assert.Equal(
            AttemptSaveStatus.AlreadyFinalized,
            lateSave);
    }


    [Fact]
    public async Task ExpiredAttempt_IsGradedOnceBeforeAnotherStart()
    {
        var (studentId, assessmentId) = await SeedAsync();
        var started = await StartAsync(studentId, assessmentId);
        var attemptId = started.AttemptId!.Value;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            // Giả lập server đã đi qua DeadlineUtc.
            await db.Attempts
                .Where(a => a.Id == attemptId)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    a => a.DeadlineUtc,
                    DateTimeOffset.UtcNow.AddSeconds(-5)));
        }

        // 5 request cùng phát hiện Attempt hết hạn.
        var results = await Task.WhenAll(
            Enumerable.Range(0, 5)
                .Select(_ => StartAsync(studentId, assessmentId)));

        Assert.Single(results,
            r => r.Status == AttemptStartStatus.ExpiredFinalized);

        // SeedAsync cấu hình MaxAttempts = 1.
        Assert.Equal(4, results.Count(
            r => r.Status == AttemptStartStatus.LimitReached));

        using var checkScope = factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var saved = await checkDb.Attempts.AsNoTracking()
            .SingleAsync(a => a.Id == attemptId);

        Assert.Equal(AttemptStatus.Graded, saved.Status);
        Assert.Equal(
            AttemptFinalizationReason.DeadlineElapsed,
            saved.FinalizationReason);

        Assert.Equal(0m, saved.OverallScore);
        Assert.NotNull(saved.FinalizedAtUtc);

        Assert.Equal(1, await checkDb.Attempts.CountAsync(
            a => a.StudentUserId == studentId &&
                 a.AssessmentId == assessmentId));
    }

    [Fact]
    public async Task LateSave_TriggersAutoFinalizeWithoutSavingAnswer()
    {
        var (studentId, assessmentId) = await SeedAsync();
        var started = await StartAsync(studentId, assessmentId);
        var attemptId = started.AttemptId!.Value;

        Guid assessmentQuestionId;
        Guid optionId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var question = await db.AssessmentQuestions
                .Where(q => q.AssessmentId == assessmentId)
                .OrderBy(q => q.OrderIndex)
                .Select(q => new { q.Id, q.QuestionId })
                .FirstAsync();

            assessmentQuestionId = question.Id;

            optionId = await db.QuestionOptions
                .Where(o => o.QuestionId == question.QuestionId &&
                            o.IsCorrect)
                .Select(o => o.Id)
                .SingleAsync();

            await db.Attempts
                .Where(a => a.Id == attemptId)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    a => a.DeadlineUtc,
                    DateTimeOffset.UtcNow.AddSeconds(-5)));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider
                .GetRequiredService<IAttemptService>();

            Assert.Equal(
                AttemptSaveStatus.Expired,
                await service.SaveAnswerAsync(
                    studentId,
                    attemptId,
                    assessmentQuestionId,
                    optionId));
        }

        using var checkScope = factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        Assert.False(await checkDb.AttemptAnswers.AnyAsync(
            a => a.AttemptId == attemptId));

        var saved = await checkDb.Attempts
            .AsNoTracking()
            .SingleAsync(a => a.Id == attemptId);

        Assert.Equal(AttemptStatus.Graded, saved.Status);
        Assert.Equal(0m, saved.OverallScore);
        Assert.Equal(
            AttemptFinalizationReason.DeadlineElapsed,
            saved.FinalizationReason);
    }


}
