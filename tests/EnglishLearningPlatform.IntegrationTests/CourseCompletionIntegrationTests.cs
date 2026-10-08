using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Commerce;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class CourseCompletionIntegrationTests(IntegrationTestFactory factory)
    : IClassFixture<IntegrationTestFactory>
{
    private sealed record Scenario(Guid Student, Guid Teacher, Guid Course, Guid Final, Guid Module, Guid[] Lessons);

    private async Task<Scenario> Seed(bool paid = false, bool roundingBoundary = false)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var visible = new Module { Title = "Visible", Lessons =
            [Lesson("One", 0), Lesson("Two", 1), Lesson("Draft", 2, LessonStatus.Draft), Lesson("Hidden", 3, LessonStatus.Hidden)] };
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Completion " + Guid.NewGuid().ToString("N"),
            Status = CourseStatus.Published, IsPaid = paid, Price = paid ? 199000 : 0,
            Modules = [visible, new Module { Title = "Hidden module", Visibility = ModuleVisibility.Hidden,
                OrderIndex = 1, Lessons = [Lesson("Unavailable published lesson", 0)] }] };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Courses.Add(course); await db.SaveChangesAsync();
        var final = new Assessment { OwnerTeacherUserId = teacher.Id, CourseId = course.Id, Title = "Final",
            Status = AssessmentStatus.Published, AssessmentType = AssessmentType.SkillAssessment,
            TargetSkill = EnglishSkill.Grammar, PassingScore = 70, DurationMinutes = 20, MaxAttempts = 3,
            Questions = Enumerable.Range(0, 5).Select(i => new AssessmentQuestion
            {
                OrderIndex = i, Points = roundingBoundary ? (i == 0 ? 6999.60m : 750.10m) : 1,
                Question = new Question { OwnerTeacherUserId = teacher.Id, Content = "Question " + i,
                    PrimarySkill = EnglishSkill.Grammar, Status = QuestionStatus.Published,
                    Options = [new QuestionOption { Content = "Correct", IsCorrect = true, OrderIndex = 0 },
                        new QuestionOption { Content = "Wrong", IsCorrect = false, OrderIndex = 1 }] }
            }).ToList() };
        db.Assessments.Add(final); await db.SaveChangesAsync();
        await db.Courses.Where(c => c.Id == course.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.FinalAssessmentId, (Guid?)final.Id));
        if (!paid)
            Assert.True((await scope.ServiceProvider.GetRequiredService<ICourseService>().EnrollFreeAsync(student.Id, course.Id)).Success);
        return new(student.Id, teacher.Id, course.Id, final.Id, visible.Id,
            visible.Lessons.Where(l => l.Status == LessonStatus.Published).Select(l => l.Id).ToArray());
    }

    private static Lesson Lesson(string title, int order, LessonStatus status = LessonStatus.Published) => new()
    {
        Title = title, OrderIndex = order, Status = status,
        Resources = [new LessonResource { ResourceType = LessonResourceType.Text, ContentText = title }]
    };

    private async Task InDb(Func<AppDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<Guid> StartAndAnswer(Scenario data, int correct = 5)
    {
        using var startScope = factory.Services.CreateScope();
        var started = await startScope.ServiceProvider.GetRequiredService<IAttemptService>().StartAsync(data.Student, data.Final);
        Assert.Equal(AttemptStartStatus.Started, started.Status);
        var attempt = started.AttemptId!.Value;
        var questions = await startScope.ServiceProvider.GetRequiredService<AppDbContext>().AssessmentQuestions.AsNoTracking()
            .Where(q => q.AssessmentId == data.Final).OrderBy(q => q.OrderIndex)
            .Select(q => new { q.Id, Options = q.Question.Options.Select(o => new { o.Id, o.IsCorrect }).ToList() }).ToListAsync();
        Assert.Equal(5, questions.Count);
        for (var i = 0; i < questions.Count; i++)
        {
            using var answerScope = factory.Services.CreateScope();
            var option = questions[i].Options.Single(o => o.IsCorrect == (i < correct)).Id;
            Assert.Equal(AttemptSaveStatus.Saved, await answerScope.ServiceProvider.GetRequiredService<IAttemptService>()
                .SaveAnswerAsync(data.Student, attempt, questions[i].Id, option));
        }
        return attempt;
    }

    private async Task<AttemptFinalizeResult> Finalize(Scenario data, Guid attempt)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAttemptService>().FinalizeAsync(data.Student, attempt);
    }

    private async Task<WriteOutcome> SetLesson(Scenario data, Guid lesson, bool completed = true)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICourseService>().SetLessonCompletedAsync(data.Student, lesson, completed);
    }

    private async Task CompleteLessons(Scenario data)
    {
        foreach (var lesson in data.Lessons) Assert.True((await SetLesson(data, lesson)).Success);
    }

    private async Task<DateTimeOffset?> CompletedAt(Scenario data)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Enrollments.AsNoTracking()
            .Where(e => e.StudentUserId == data.Student && e.CourseId == data.Course).Select(e => e.CompletedAtUtc).SingleAsync();
    }

    private async Task<Attempt> PersistedAttempt(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Attempts.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    private async Task<EnglishLearningPlatform.Application.Learning.CourseProgress> Progress(Scenario data)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICourseService>().OpenCourseAsync(data.Student, data.Course, false);
        Assert.Equal(LearningAccessResult.Allowed, result.Access);
        return result.Value!.Progress!;
    }

    [Fact]
    public async Task AllEffectiveLessonsWithoutGradedFinal_DoNotCompleteCourse()
    {
        var data = await Seed(); await CompleteLessons(data);
        var progress = await Progress(data);
        Assert.Equal(2, progress.TotalLessons); Assert.Equal(2, progress.CompletedLessons);
        Assert.Equal(100m, progress.Percentage); Assert.Null(progress.CompletedAtUtc);
        var attempt = await StartAndAnswer(data);
        Assert.Equal(AttemptStatus.InProgress, (await PersistedAttempt(attempt)).Status);
        Assert.Null(await CompletedAt(data));
    }

    [Fact]
    public async Task FailedFinalAfterLessons_DoesNotCompleteCourse()
    {
        var data = await Seed(); await CompleteLessons(data);
        var result = await Finalize(data, await StartAndAnswer(data, 3));
        Assert.Equal(AttemptFinalizeStatus.Graded, result.Status); Assert.Equal(60m, result.OverallScore);
        Assert.False(result.Passed); Assert.Null(await CompletedAt(data));
    }

    [Fact]
    public async Task PassingFinalAfterLessons_CompletesImmediatelyWithoutAnotherLessonRequest()
    {
        var data = await Seed(); await CompleteLessons(data); Assert.Null(await CompletedAt(data));
        var before = DateTimeOffset.UtcNow;
        var result = await Finalize(data, await StartAndAnswer(data));
        Assert.Equal(AttemptFinalizeStatus.Graded, result.Status); Assert.True(result.Passed); Assert.Equal(100m, result.OverallScore);
        var completed = await CompletedAt(data); Assert.NotNull(completed);
        Assert.InRange(completed.Value, before, DateTimeOffset.UtcNow);
        Assert.Equal(100m, (await Progress(data)).Percentage);
    }

    [Fact]
    public async Task PassingFinalBeforeLessons_CompletesWhenLastEffectiveLessonIsRecorded()
    {
        var data = await Seed(); var result = await Finalize(data, await StartAndAnswer(data));
        Assert.True(result.Passed); Assert.Null(await CompletedAt(data));
        Assert.True((await SetLesson(data, data.Lessons[0])).Success); Assert.Null(await CompletedAt(data));
        Assert.True((await SetLesson(data, data.Lessons[1])).Success); Assert.NotNull(await CompletedAt(data));
    }

    [Fact]
    public async Task ConcurrentFinalizationAndLessonWrites_GradeOnceAndPreserveCompletionTimestamp()
    {
        var data = await Seed(); Assert.True((await SetLesson(data, data.Lessons[0])).Success);
        var attempt = await StartAndAnswer(data);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finals = Enumerable.Range(0, 5).Select(async _ => { await gate.Task; return await Finalize(data, attempt); }).ToArray();
        var lessons = Enumerable.Range(0, 5).Select(async _ => { await gate.Task; return await SetLesson(data, data.Lessons[1]); }).ToArray();
        gate.SetResult(); await Task.WhenAll(finals.Cast<Task>().Concat(lessons));
        _ = Assert.Single(finals, t => t.Result.Status == AttemptFinalizeStatus.Graded);
        Assert.Equal(4, finals.Count(t => t.Result.Status == AttemptFinalizeStatus.AlreadyGraded));
        Assert.All(finals, t => { Assert.Equal(100m, t.Result.OverallScore); Assert.True(t.Result.Passed); });
        Assert.All(lessons, t => Assert.True(t.Result.Success));
        var completed = await CompletedAt(data); Assert.NotNull(completed);
        var saved = await PersistedAttempt(attempt); Assert.Equal(AttemptStatus.Graded, saved.Status);
        Assert.Equal(AttemptFinalizeStatus.AlreadyGraded, (await Finalize(data, attempt)).Status);
        Assert.Equal(completed, await CompletedAt(data)); Assert.Equal(saved.FinalizedAtUtc, (await PersistedAttempt(attempt)).FinalizedAtUtc);
        await InDb(async db =>
        {
            Assert.Equal(1, await db.Attempts.CountAsync(a => a.StudentUserId == data.Student && a.AssessmentId == data.Final));
            Assert.Equal(2, await db.LessonProgressEntries.CountAsync(p => p.Enrollment.StudentUserId == data.Student && p.Enrollment.CourseId == data.Course));
        });
    }

    [Fact]
    public async Task PassingFinalWithNoEffectiveLessons_DoesNotCompleteCourse()
    {
        var data = await Seed();
        await InDb(db => db.Modules.Where(m => m.CourseId == data.Course)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Visibility, ModuleVisibility.Hidden)));
        Assert.True((await Finalize(data, await StartAndAnswer(data))).Passed);
        var progress = await Progress(data); Assert.Equal(0, progress.TotalLessons);
        Assert.Null(progress.Percentage); Assert.Null(progress.CompletedAtUtc);
    }

    [Fact]
    public async Task CourseChangesAndIncompleteLesson_PreserveHistoricalCompletion()
    {
        var data = await Seed(); await CompleteLessons(data); await Finalize(data, await StartAndAnswer(data));
        var completed = await CompletedAt(data); Assert.NotNull(completed);
        await InDb(async db => { db.Lessons.Add(new Lesson { ModuleId = data.Module, Title = "Added after completion", OrderIndex = 4,
            Status = LessonStatus.Published }); await db.SaveChangesAsync(); });
        Assert.True((await SetLesson(data, data.Lessons[0], false)).Success);
        var progress = await Progress(data); Assert.Equal(3, progress.TotalLessons);
        Assert.Equal(1, progress.CompletedLessons); Assert.Equal(completed, progress.CompletedAtUtc);
    }

    [Fact]
    public async Task RoundedDisplayScoreAtPassingBoundary_DoesNotCompleteFailedFinal()
    {
        var data = await Seed(roundingBoundary: true); await CompleteLessons(data);
        // Raw score 6999.60 / 10000 * 100 = 69.996, display score = 70.00.
        var result = await Finalize(data, await StartAndAnswer(data, 1));
        Assert.Equal(70m, result.OverallScore); Assert.False(result.Passed);
        Assert.Null(await CompletedAt(data));
        Assert.True((await SetLesson(data, data.Lessons[0])).Success); Assert.Null(await CompletedAt(data));
    }

    [Theory]
    [InlineData("wrong-course")]
    [InlineData("wrong-owner")]
    [InlineData("placement")]
    [InlineData("unpublished")]
    [InlineData("archived")]
    [InlineData("missing-passing-score")]
    public async Task InvalidFinalDefinition_DoesNotCompleteDespiteExistingPassingResult(string invalid)
    {
        var data = await Seed(); Assert.True((await Finalize(data, await StartAndAnswer(data))).Passed);
        // Corrupt only the definition after a real grade, to exercise completion guards without changing authoring APIs.
        var foreignOwner = invalid == "wrong-owner" ? (await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher)).Id : data.Teacher;
        await InDb(async db =>
        {
            var final = await db.Assessments.SingleAsync(a => a.Id == data.Final);
            switch (invalid)
            {
                case "wrong-course":
                    var other = new Course { OwnerTeacherUserId = data.Teacher, Title = "Different course" };
                    db.Courses.Add(other); await db.SaveChangesAsync(); final.CourseId = other.Id; break;
                case "wrong-owner": final.OwnerTeacherUserId = foreignOwner; break;
                case "placement": final.AssessmentType = AssessmentType.PlacementTest; final.PassingScore = null; break;
                case "unpublished": final.Status = AssessmentStatus.Unpublished; break;
                case "archived": final.Status = AssessmentStatus.Archived; break;
                case "missing-passing-score": final.PassingScore = null; break;
            }
            await db.SaveChangesAsync();
        });
        await CompleteLessons(data); Assert.Null(await CompletedAt(data));
    }

    [Fact]
    public async Task HighestPassingFinal_CompletesDespiteLaterFailedAttempt()
    {
        var data = await Seed();
        var highest = await Finalize(data, await StartAndAnswer(data, 4));
        Assert.Equal(80m, highest.OverallScore); Assert.True(highest.Passed);
        Assert.Null(await CompletedAt(data));
        var latest = await Finalize(data, await StartAndAnswer(data, 3));
        Assert.Equal(60m, latest.OverallScore); Assert.False(latest.Passed);
        Assert.Null(await CompletedAt(data));

        await CompleteLessons(data);

        Assert.NotNull(await CompletedAt(data));
        await InDb(async db => Assert.Equal(2, await db.Attempts.CountAsync(a =>
            a.StudentUserId == data.Student && a.AssessmentId == data.Final && a.Status == AttemptStatus.Graded)));
    }

    [Fact]
    public async Task VerifiedHostedPaymentThroughLearningAndRealFinal_RecordsCourseCompletion()
    {
        var data = await Seed(paid: true);
        var options = factory.Services.GetRequiredService<IOptions<HostedPaymentOptions>>().Value;
        options.Enabled = true; options.Provider = "CompletionFixture";
        options.SigningSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        options.CheckoutUrl = "https://gateway.example.test/checkout"; options.PublicBaseUrl = "https://learning.example.test";
        try
        {
            Guid orderId;
            using (var scope = factory.Services.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
                var order = await service.StartAsync(data.Student, data.Course); orderId = order.OrderId!.Value;
                Assert.Null(order.Error);
                var body = JsonSerializer.SerializeToUtf8Bytes(new PaymentNotification(orderId, "completion-" + Guid.NewGuid().ToString("N"),
                    199000, "VND", PaymentStatus.Succeeded, DateTimeOffset.UtcNow), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.SigningSecret), body));
                using var client = IdentityTestHelpers.CreateClient(factory);
                using var request = new HttpRequestMessage(HttpMethod.Post, "/payments/webhook") { Content = new ByteArrayContent(body) };
                request.Headers.Add("X-Payment-Signature", signature);
                using var response = await client.SendAsync(request); Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            }
            await CompleteLessons(data); Assert.Null(await CompletedAt(data));
            Assert.True((await Finalize(data, await StartAndAnswer(data))).Passed); Assert.NotNull(await CompletedAt(data));
            await InDb(async db =>
            {
                var enrollment = await db.Enrollments.Include(e => e.Payment).SingleAsync(e => e.StudentUserId == data.Student && e.CourseId == data.Course);
                Assert.Equal(orderId, enrollment.Payment!.OrderId); Assert.Equal(PaymentStatus.Succeeded, enrollment.Payment.Status);
                Assert.Equal(1, await db.Enrollments.CountAsync(e => e.StudentUserId == data.Student && e.CourseId == data.Course));
            });
        }
        finally { options.Enabled = false; }
    }

    private async Task AssignInvalidPayment(Scenario data, bool pending)
    {
        await InDb(async db =>
        {
            await db.Courses.Where(c => c.Id == data.Course)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsPaid, true).SetProperty(c => c.Price, 199000m));
            var payment = new Payment { Order = new Order { StudentUserId = data.Student, CourseId = data.Course,
                    Amount = 199000, Currency = "VND", Provider = "CompletionFixture", ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30) },
                Amount = pending ? 199000 : 1, Status = pending ? PaymentStatus.Pending : PaymentStatus.Succeeded,
                Provider = "CompletionFixture", ProviderTransactionId = "invalid-" + Guid.NewGuid().ToString("N") };
            db.Payments.Add(payment);
            var enrollment = await db.Enrollments.SingleOrDefaultAsync(e => e.StudentUserId == data.Student && e.CourseId == data.Course);
            if (enrollment is null) db.Enrollments.Add(new Enrollment { StudentUserId = data.Student, CourseId = data.Course, Payment = payment });
            else enrollment.Payment = payment;
            await db.SaveChangesAsync();
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidPaidEnrollment_BlocksStartAndLessonProgress(bool pending)
    {
        var data = await Seed(paid: true); await AssignInvalidPayment(data, pending);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(AttemptStartStatus.Forbidden, (await scope.ServiceProvider.GetRequiredService<IAttemptService>()
            .StartAsync(data.Student, data.Final)).Status);
        Assert.Equal(LearningAccessResult.Forbidden, (await SetLesson(data, data.Lessons[0])).Access);
        Assert.Null(await CompletedAt(data));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PaymentInvalidatedBeforeFinalize_DoesNotRecordCourseCompletion(bool pending)
    {
        var data = await Seed(); await CompleteLessons(data); var attempt = await StartAndAnswer(data);
        await AssignInvalidPayment(data, pending);
        var result = await Finalize(data, attempt);
        Assert.Equal(AttemptFinalizeStatus.Graded, result.Status); Assert.True(result.Passed);
        Assert.Null(await CompletedAt(data));
        Assert.Equal(LearningAccessResult.Forbidden, (await SetLesson(data, data.Lessons[0])).Access);
    }

    [Fact]
    public async Task CompletionUpdateFailure_RollsBackGradeAndCompletion_ThenNewScopeRetrySucceeds()
    {
        var data = await Seed(); await CompleteLessons(data); var attempt = await StartAndAnswer(data);
        var fault = new CompletionUpdateFailureInterceptor { AttemptId = attempt };
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(fault))));
        using (var scope = host.Services.CreateScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IAttemptService>()
                .FinalizeAsync(data.Student, attempt));
        Assert.True(fault.SawGradedAttemptInTransaction); Assert.Equal(1, fault.Failures);
        var saved = await PersistedAttempt(attempt);
        Assert.Equal(AttemptStatus.InProgress, saved.Status); Assert.Null(saved.OverallScore);
        Assert.Null(saved.Passed); Assert.Null(saved.FinalizedAtUtc); Assert.Null(await CompletedAt(data));
        Assert.Equal(AttemptFinalizeStatus.Graded, (await Finalize(data, attempt)).Status);
        Assert.NotNull(await CompletedAt(data)); Assert.True((await PersistedAttempt(attempt)).Passed);
    }

    [Fact]
    public async Task AlreadyGradedReplay_RepairsMissedCompletionWithoutChangingGradeOrFinalization()
    {
        var data = await Seed(); await CompleteLessons(data); var attempt = await StartAndAnswer(data);
        Assert.Equal(AttemptFinalizeStatus.Graded, (await Finalize(data, attempt)).Status);
        var graded = await PersistedAttempt(attempt); Assert.NotNull(await CompletedAt(data));
        // Simulate a historical grade created before the completion integration existed.
        await InDb(db => db.Enrollments.Where(e => e.StudentUserId == data.Student && e.CourseId == data.Course)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.CompletedAtUtc, (DateTimeOffset?)null)));
        var replay = await Finalize(data, attempt); Assert.Equal(AttemptFinalizeStatus.AlreadyGraded, replay.Status);
        var repaired = await CompletedAt(data); Assert.NotNull(repaired);
        var saved = await PersistedAttempt(attempt); Assert.Equal(graded.OverallScore, saved.OverallScore);
        Assert.Equal(graded.Passed, saved.Passed); Assert.Equal(graded.FinalizedAtUtc, saved.FinalizedAtUtc);
        Assert.Equal(AttemptFinalizeStatus.AlreadyGraded, (await Finalize(data, attempt)).Status);
        Assert.Equal(repaired, await CompletedAt(data));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExpiredFinal_GradedFromStartOrLateSave_ReconcilesCompletion(bool finalizeFromStart)
    {
        var data = await Seed(); await CompleteLessons(data); var attempt = await StartAndAnswer(data);
        await InDb(db => db.Attempts.Where(a => a.Id == attempt)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.DeadlineUtc, DateTimeOffset.UtcNow.AddMinutes(-1))));
        using var scope = factory.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<IAttemptService>();
        if (finalizeFromStart)
            Assert.Equal(AttemptStartStatus.ExpiredFinalized, (await service.StartAsync(data.Student, data.Final)).Status);
        else
            Assert.Equal(AttemptSaveStatus.Expired, await service.SaveAnswerAsync(data.Student, attempt, Guid.NewGuid(), null));
        var saved = await PersistedAttempt(attempt); Assert.True(saved.Passed);
        Assert.Equal(AttemptFinalizationReason.DeadlineElapsed, saved.FinalizationReason); Assert.NotNull(await CompletedAt(data));
    }

    private sealed class CompletionUpdateFailureInterceptor : DbCommandInterceptor
    {
        public Guid AttemptId { get; init; }
        public bool SawGradedAttemptInTransaction { get; private set; }
        public int Failures { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("[Enrollments]", StringComparison.Ordinal) &&
                command.CommandText.Contains("[CompletedAtUtc]", StringComparison.Ordinal))
            {
                await using var probe = command.Connection!.CreateCommand();
                probe.Transaction = command.Transaction;
                probe.CommandText = "SELECT COUNT(*) FROM [Attempts] WHERE [Id] = @id AND [Status] = N'Graded' AND [Passed] = 1";
                var id = probe.CreateParameter(); id.ParameterName = "@id"; id.Value = AttemptId; probe.Parameters.Add(id);
                SawGradedAttemptInTransaction = Convert.ToInt32(await probe.ExecuteScalarAsync(cancellationToken)) == 1;
                Failures++;
                throw new InvalidOperationException("Injected completion update failure after grade save.");
            }
            return result;
        }
    }
}
