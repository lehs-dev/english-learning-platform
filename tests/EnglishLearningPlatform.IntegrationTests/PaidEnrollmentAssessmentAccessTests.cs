using System.Text.Json;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Commerce;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class PaidEnrollmentAssessmentAccessTests(CheckoutTestFactory factory)
    : IClassFixture<CheckoutTestFactory>
{
    private sealed record PaidCourse(Guid StudentId, Course Course, Assessment Assessment);

    private async Task<PaidCourse> SeedAsync(bool weightedVocabulary = false)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var course = new Course
        {
            OwnerTeacherUserId = teacher.Id,
            Title = "Paid final assessment " + Guid.NewGuid().ToString("N"),
            IsPaid = true,
            Price = 199000m,
            Status = CourseStatus.Published,
            Modules =
            [
                new Module
                {
                    Title = "Everyday English",
                    Lessons = Enumerable.Range(0, 2).Select(index => new Lesson
                    {
                        Title = $"Lesson {index + 1}",
                        OrderIndex = index,
                        Status = LessonStatus.Published,
                        Resources = [new LessonResource { ResourceType = LessonResourceType.Text, ContentText = "Lesson content" }]
                    }).ToList()
                }
            ]
        };
        var assessment = new Assessment
        {
            OwnerTeacherUserId = teacher.Id,
            Course = course,
            Title = "Paid course final",
            Description = "Five grammar and vocabulary questions",
            AssessmentType = AssessmentType.PracticeExam,
            Status = AssessmentStatus.Published,
            PassingScore = 80m,
            MaxAttempts = 3,
            DurationMinutes = 15,
            Questions = Enumerable.Range(0, 5).Select(index => new AssessmentQuestion
            {
                OrderIndex = index,
                Points = weightedVocabulary && index == 3 ? 2m : 1m,
                Question = new Question
                {
                    OwnerTeacherUserId = teacher.Id,
                    Content = $"Question {index + 1}",
                    PrimarySkill = index < 3 ? EnglishSkill.Grammar : EnglishSkill.Vocabulary,
                    Status = QuestionStatus.Published,
                    Explanation = "Private explanation for teachers",
                    Options =
                    [
                        new QuestionOption { OrderIndex = 0, Content = "Correct option", IsCorrect = true },
                        new QuestionOption { OrderIndex = 1, Content = "Incorrect option" }
                    ]
                }
            }).ToList()
        };

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Courses.Add(course);
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();
        await db.Courses.Where(c => c.Id == course.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.FinalAssessmentId, (Guid?)assessment.Id));
        return new(student.Id, course, assessment);
    }

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private Task<AttemptStartResult> StartAsync(PaidCourse data) => InScopeAsync(services =>
        services.GetRequiredService<IAttemptService>().StartAsync(data.StudentId, data.Assessment.Id));

    private Task<AttemptFinalizeResult> FinalizeAsync(PaidCourse data, Guid attemptId) => InScopeAsync(services =>
        services.GetRequiredService<IAttemptService>().FinalizeAsync(data.StudentId, attemptId));

    private Task<CheckoutCode> ProcessAsync(SignedPaymentCallback callback) => InScopeAsync(services =>
        services.GetRequiredService<ICheckoutService>().ProcessCallbackAsync(callback));

    private async Task<OrderCheckout> CreateOrderAsync(PaidCourse data)
    {
        var created = await InScopeAsync(services => services.GetRequiredService<ICheckoutService>()
            .CreateOrderAsync(data.StudentId, data.Course.Id));
        Assert.Equal(CheckoutCode.Allowed, created.Code);
        return Assert.IsType<OrderCheckout>(created.Value);
    }

    private SignedPaymentCallback Callback(OrderCheckout order, SandboxScenario scenario = SandboxScenario.Success) =>
        factory.Services.GetRequiredService<LocalSandboxGateway>().CreateCallback(order, scenario);

    private async Task CompleteLessonsAsync(PaidCourse data)
    {
        foreach (var lesson in data.Course.Modules.Single().Lessons)
        {
            var completion = await InScopeAsync(services => services.GetRequiredService<ICourseService>()
                .SetLessonCompletedAsync(data.StudentId, lesson.Id, true));
            Assert.True(completion.Success);
        }
    }

    private async Task SaveAnswersAsync(PaidCourse data, Guid attemptId)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAttemptService>();
        foreach (var question in data.Assessment.Questions.OrderBy(q => q.OrderIndex))
        {
            // Four correct answers; the final vocabulary answer is wrong.
            var option = question.Question.Options.Single(o => o.IsCorrect == (question.OrderIndex < 4));
            Assert.Equal(AttemptSaveStatus.Saved,
                await service.SaveAnswerAsync(data.StudentId, attemptId, question.Id, option.Id));
        }
    }

    [Theory]
    [InlineData(false, 80, 50)]
    [InlineData(true, 83.33, 66.67)]
    public async Task VerifiedPayment_ProgressAndPassingFinal_ReconcileCompletionAndPersistOneResult(
        bool weightedVocabulary, double expectedOverall, double expectedVocabulary)
    {
        var data = await SeedAsync(weightedVocabulary);
        var order = await CreateOrderAsync(data);
        var callback = Callback(order);
        var callbacks = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ProcessAsync(callback)));
        Assert.All(callbacks, result => Assert.Equal(CheckoutCode.Allowed, result));

        await CompleteLessonsAsync(data);
        var progress = await InScopeAsync(services => services.GetRequiredService<ICourseService>()
            .OpenCourseAsync(data.StudentId, data.Course.Id, false));
        Assert.Equal(100m, progress.Value!.Progress!.Percentage);
        Assert.Null(progress.Value.Progress.CompletedAtUtc);

        var starts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => StartAsync(data)));
        Assert.Single(starts, result => result.Status == AttemptStartStatus.Started);
        Assert.All(starts, result => Assert.True(result.Status is AttemptStartStatus.Started or AttemptStartStatus.Resumed));
        var attemptId = starts[0].AttemptId!.Value;
        Assert.All(starts, result => Assert.Equal(attemptId, result.AttemptId));

        using (var scope = factory.Services.CreateScope())
        {
            var queries = scope.ServiceProvider.GetRequiredService<IAssessmentQueryService>();
            var overview = await queries.GetAssessmentOverviewAsync(data.StudentId, data.Assessment.Id);
            Assert.NotNull(overview);
            Assert.Equal(1, overview.UsedAttempts);
            Assert.Equal(attemptId, overview.ActiveAttemptId);
            Assert.Null(overview.HighestScore);
            var taking = await queries.GetTakingSheetAsync(data.StudentId, attemptId);
            Assert.NotNull(taking);
            Assert.Equal(5, taking.Questions.Count);
            Assert.All(taking.Questions, question => Assert.Null(question.SelectedOptionId));
            var json = JsonSerializer.Serialize(taking);
            Assert.DoesNotContain("IsCorrect", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Private explanation", json);
            Assert.Null(await queries.GetAttemptResultAsync(data.StudentId, attemptId));
        }

        await SaveAnswersAsync(data, attemptId);
        using (var scope = factory.Services.CreateScope())
        {
            var sheet = await scope.ServiceProvider.GetRequiredService<IAssessmentQueryService>()
                .GetTakingSheetAsync(data.StudentId, attemptId);
            Assert.NotNull(sheet);
            Assert.All(sheet.Questions, question => Assert.NotNull(question.SelectedOptionId));
        }

        var submitted = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => FinalizeAsync(data, attemptId)));
        Assert.Single(submitted, result => result.Status == AttemptFinalizeStatus.Graded);
        Assert.Equal(3, submitted.Count(result => result.Status == AttemptFinalizeStatus.AlreadyGraded));
        Assert.All(submitted, result =>
        {
            Assert.Equal((decimal)expectedOverall, result.OverallScore);
            Assert.True(result.Passed);
            Assert.Equal(AttemptFinalizationReason.ManualSubmit, result.Reason);
        });

        DateTimeOffset finalizedAt;
        DateTimeOffset completedAt;
        Guid[] skillResultIds;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var queries = scope.ServiceProvider.GetRequiredService<IAssessmentQueryService>();
            var enrollment = await db.Enrollments.AsNoTracking().Include(e => e.Payment).ThenInclude(p => p!.Order)
                .SingleAsync(e => e.StudentUserId == data.StudentId && e.CourseId == data.Course.Id);
            Assert.NotNull(enrollment.CompletedAtUtc);
            completedAt = enrollment.CompletedAtUtc.Value;
            Assert.Equal(PaymentStatus.Succeeded, enrollment.Payment!.Status);
            Assert.NotNull(enrollment.Payment.PaidAtUtc);
            Assert.Equal(data.StudentId, enrollment.Payment.Order.StudentUserId);
            Assert.Equal(order.Id, enrollment.Payment.OrderId);
            Assert.Equal(order.Amount, enrollment.Payment.Amount);

            var result = await queries.GetAttemptResultAsync(data.StudentId, attemptId);
            Assert.NotNull(result);
            Assert.Equal(data.Course.Id, result.CourseId);
            Assert.Equal(80m, result.PassingScore);
            Assert.Equal((decimal)expectedOverall, result.OverallScore);
            Assert.True(result.Passed);
            finalizedAt = result.FinalizedAtUtc;
            var grammar = Assert.Single(result.SkillBreakdowns);
            Assert.Equal(EnglishSkill.Grammar, grammar.Skill);
            Assert.Equal(3, grammar.QuestionCount);
            Assert.Equal(100m, grammar.Score);
            Assert.False(grammar.IsWeak);
            // Chỉ ẩn Vocabulary 2 câu khi trả kết quả; không bỏ kiểm tra điểm đã lưu.
            var vocabulary = await db.AttemptSkillResults.AsNoTracking().SingleAsync(skill =>
                skill.AttemptId == attemptId && skill.Skill == EnglishSkill.Vocabulary);
            Assert.Equal(2, vocabulary.QuestionCount);
            Assert.Equal((decimal)expectedVocabulary, vocabulary.Score);
            Assert.Null(await queries.GetTakingSheetAsync(data.StudentId, attemptId));
            var overview = await queries.GetAssessmentOverviewAsync(data.StudentId, data.Assessment.Id);
            Assert.NotNull(overview);
            Assert.Equal((decimal)expectedOverall, overview.HighestScore);
            Assert.Null(overview.ActiveAttemptId);
            skillResultIds = await db.AttemptSkillResults.Where(s => s.AttemptId == attemptId)
                .OrderBy(s => s.Id).Select(s => s.Id).ToArrayAsync();
            Assert.Equal(2, skillResultIds.Length);
        }

        // Retry the payment and submit after completion: both preserve history.
        Assert.Equal(CheckoutCode.Allowed, await ProcessAsync(callback));
        Assert.Equal(AttemptFinalizeStatus.AlreadyGraded, (await FinalizeAsync(data, attemptId)).Status);
        var otherStudent = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var verification = factory.Services.CreateScope();
        var checkDb = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        var checkQueries = verification.ServiceProvider.GetRequiredService<IAssessmentQueryService>();
        Assert.Equal(1, await checkDb.Orders.CountAsync(o => o.CourseId == data.Course.Id));
        Assert.Equal(1, await checkDb.Payments.CountAsync(p => p.OrderId == order.Id));
        Assert.Equal(1, await checkDb.Enrollments.CountAsync(e => e.CourseId == data.Course.Id));
        Assert.Equal(1, await checkDb.Attempts.CountAsync(a => a.AssessmentId == data.Assessment.Id));
        Assert.Equal(5, await checkDb.AttemptAnswers.CountAsync(a => a.AttemptId == attemptId));
        Assert.Equal(skillResultIds, await checkDb.AttemptSkillResults.Where(s => s.AttemptId == attemptId)
            .OrderBy(s => s.Id).Select(s => s.Id).ToArrayAsync());
        Assert.Equal(finalizedAt, (await checkQueries.GetAttemptResultAsync(data.StudentId, attemptId))!.FinalizedAtUtc);
        Assert.Equal(completedAt, await checkDb.Enrollments.Where(e => e.CourseId == data.Course.Id)
            .Select(e => e.CompletedAtUtc).SingleAsync());
        Assert.Null(await checkQueries.GetTakingSheetAsync(otherStudent.Id, attemptId));
        Assert.Null(await checkQueries.GetAttemptResultAsync(otherStudent.Id, attemptId));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("unverified")]
    public async Task PaymentWithoutVerifiedSuccess_DeniesEnrollmentLearningAndAssessment(string state)
    {
        var data = await SeedAsync();
        var order = await CreateOrderAsync(data);
        if (state != "pending")
        {
            var callback = Callback(order, state == "failed" ? SandboxScenario.Failed :
                state == "cancelled" ? SandboxScenario.Cancelled : SandboxScenario.Success);
            if (state == "unverified") callback = callback with { Signature = "invalid" };
            Assert.Equal(state == "unverified" ? CheckoutCode.InvalidCallback : CheckoutCode.Allowed,
                await ProcessAsync(callback));
        }

        Assert.Equal(AttemptStartStatus.Forbidden, (await StartAsync(data)).Status);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queries = scope.ServiceProvider.GetRequiredService<IAssessmentQueryService>();
        Assert.Null(await queries.GetAssessmentOverviewAsync(data.StudentId, data.Assessment.Id));
        Assert.Equal(LearningAccessResult.Forbidden, (await scope.ServiceProvider.GetRequiredService<ICourseService>()
            .OpenLessonAsync(data.StudentId, data.Course.Modules.Single().Lessons.First().Id)).Access);
        Assert.False(await db.Enrollments.AnyAsync(e => e.CourseId == data.Course.Id));
        Assert.False(await db.Attempts.AnyAsync(a => a.AssessmentId == data.Assessment.Id));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("buyer")]
    [InlineData("course")]
    [InlineData("amount")]
    [InlineData("currency")]
    public async Task InvalidPaidEnrollment_DeniesResumeAndTakingQueries(string invalid)
    {
        var data = await SeedAsync();
        var order = await CreateOrderAsync(data);
        Assert.Equal(CheckoutCode.Allowed, await ProcessAsync(Callback(order)));
        var started = await StartAsync(data);
        Assert.Equal(AttemptStartStatus.Started, started.Status);
        var other = invalid == "buyer"
            ? await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student) : null;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var payment = await db.Payments.Include(p => p.Order).SingleAsync(p => p.OrderId == order.Id);
            switch (invalid)
            {
                case "pending": payment.Status = PaymentStatus.Pending; break;
                case "failed": payment.Status = PaymentStatus.Failed; break;
                case "cancelled": payment.Status = PaymentStatus.Cancelled; break;
                case "buyer": payment.Order.StudentUserId = other!.Id; break;
                case "course":
                    var foreignCourse = new Course { OwnerTeacherUserId = data.Course.OwnerTeacherUserId,
                        Title = "Foreign payment course", Status = CourseStatus.Published };
                    db.Courses.Add(foreignCourse);
                    payment.Order.Course = foreignCourse;
                    break;
                case "amount": payment.Amount += 1m; break;
                case "currency": payment.Order.Currency = "USD"; break;
            }
            await db.SaveChangesAsync();
        }

        Assert.Equal(AttemptStartStatus.Forbidden, (await StartAsync(data)).Status);
        using var verification = factory.Services.CreateScope();
        var queries = verification.ServiceProvider.GetRequiredService<IAssessmentQueryService>();
        Assert.Null(await queries.GetAssessmentOverviewAsync(data.StudentId, data.Assessment.Id));
        Assert.Null(await queries.GetTakingSheetAsync(data.StudentId, started.AttemptId!.Value));
        var dbCheck = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempt = Assert.Single(await dbCheck.Attempts.AsNoTracking()
            .Where(a => a.AssessmentId == data.Assessment.Id).ToListAsync());
        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
        Assert.Equal(started.DeadlineUtc, attempt.DeadlineUtc);
        Assert.Null(attempt.FinalizedAtUtc);
        Assert.Null(await dbCheck.Enrollments.Where(e => e.CourseId == data.Course.Id)
            .Select(e => e.CompletedAtUtc).SingleAsync());
    }

    [Fact]
    public async Task DeadlineElapsed_FinalizesSavedAnswersAndCompletionOnce_WithoutSavingLateAnswer()
    {
        var data = await SeedAsync();
        var order = await CreateOrderAsync(data);
        Assert.Equal(CheckoutCode.Allowed, await ProcessAsync(Callback(order)));
        await CompleteLessonsAsync(data);
        var started = await StartAsync(data);
        Assert.Equal(AttemptStartStatus.Started, started.Status);
        var attemptId = started.AttemptId!.Value;
        await SaveAnswersAsync(data, attemptId);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Attempts.Where(a => a.Id == attemptId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.DeadlineUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IAssessmentQueryService>()
                .GetTakingSheetAsync(data.StudentId, attemptId));
            Assert.Equal(AttemptStatus.InProgress, await db.Attempts.Where(a => a.Id == attemptId)
                .Select(a => a.Status).SingleAsync());
        }

        var first = data.Assessment.Questions.OrderBy(q => q.OrderIndex).First();
        Assert.Equal(AttemptSaveStatus.Expired, await InScopeAsync(services => services.GetRequiredService<IAttemptService>()
            .SaveAnswerAsync(data.StudentId, attemptId, first.Id, first.Question.Options.Single(o => !o.IsCorrect).Id)));
        var repeated = await FinalizeAsync(data, attemptId);
        Assert.Equal(AttemptFinalizeStatus.AlreadyGraded, repeated.Status);
        Assert.Equal(80m, repeated.OverallScore);
        Assert.True(repeated.Passed);
        Assert.Equal(AttemptFinalizationReason.DeadlineElapsed, repeated.Reason);
        using var verification = factory.Services.CreateScope();
        var checkDb = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(first.Question.Options.Single(o => o.IsCorrect).Id,
            await checkDb.AttemptAnswers.Where(a => a.AttemptId == attemptId && a.AssessmentQuestionId == first.Id)
                .Select(a => a.SelectedOptionId).SingleAsync());
        Assert.NotNull(await checkDb.Enrollments.Where(e => e.CourseId == data.Course.Id)
            .Select(e => e.CompletedAtUtc).SingleAsync());
        Assert.Equal(2, await checkDb.AttemptSkillResults.CountAsync(s => s.AttemptId == attemptId));
        var result = await verification.ServiceProvider.GetRequiredService<IAssessmentQueryService>()
            .GetAttemptResultAsync(data.StudentId, attemptId);
        Assert.NotNull(result);
        Assert.Equal(80m, result.OverallScore);
        Assert.Equal(AttemptFinalizationReason.DeadlineElapsed, result.FinalizationReason);
    }
}
