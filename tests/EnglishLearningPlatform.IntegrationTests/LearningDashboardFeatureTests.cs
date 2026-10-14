using System.Net;
using System.Text.Json;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class LearningDashboardFeatureTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    private async Task<Data> SeedAsync()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new Course { Title = "Reading course " + Guid.NewGuid().ToString("N"), OwnerTeacherUserId = teacher.Id,
            Status = CourseStatus.Published, Skills = [new CourseSkill { Skill = EnglishSkill.Reading }] };
        var module = new Module { Course = course, Title = "Visible module", Visibility = ModuleVisibility.Visible };
        var first = new Lesson { Module = module, Title = "First lesson", Status = LessonStatus.Published,
            Resources = [new LessonResource { ResourceType = LessonResourceType.Text, ContentText = "Lesson content" }] };
        var second = new Lesson { Module = module, Title = "Second lesson", Status = LessonStatus.Published, OrderIndex = 1 };
        var enrollment = new Enrollment { StudentUserId = student.Id, Course = course, LastAccessedLesson = second };
        var practice = new Practice { Lesson = first, OwnerTeacherUserId = teacher.Id, Status = PracticeStatus.Published,
            Title = "Reading practice", Questions = Enumerable.Range(0, 2).Select(i => new PracticeQuestion
            { OrderIndex = i, Question = Question(teacher.Id, EnglishSkill.Reading, i) }).ToList() };
        var assessment = new Assessment { Course = course, OwnerTeacherUserId = teacher.Id, Status = AssessmentStatus.Published,
            AssessmentType = AssessmentType.PracticeExam, DurationMinutes = 15, MaxAttempts = 10, PassingScore = 80,
            Title = "Reading assessment", Questions = Enumerable.Range(0, 5).Select(i => new AssessmentQuestion
            { OrderIndex = i, Points = 1, Question = Question(teacher.Id, i < 3 ? EnglishSkill.Reading : EnglishSkill.Grammar, i) }).ToList() };
        db.Enrollments.Add(enrollment);
        db.Lessons.AddRange(first, second);
        db.Practices.Add(practice);
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();
        return new(teacher, student, course, module, first, second, enrollment, practice, assessment);
    }

    private static Question Question(Guid teacherId, EnglishSkill skill, int i) => new()
    {
        OwnerTeacherUserId = teacherId, PrimarySkill = skill, Status = QuestionStatus.Published, Content = "Question " + i,
        Explanation = "PRIVATE_PRACTICE_EXPLANATION", Options =
        [new QuestionOption { Content = "Correct " + i, IsCorrect = true }, new QuestionOption { Content = "Wrong " + i, OrderIndex = 1 }]
    };

    private static Attempt Graded(Data data, decimal overall, int correctReading, Guid? id = null, DateTimeOffset? finalized = null) => new()
    {
        Id = id ?? Guid.NewGuid(), StudentUserId = data.Student.Id, AssessmentId = data.Assessment.Id, Status = AttemptStatus.Graded,
        StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20), DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
        OverallScore = overall, Passed = overall >= 80, FinalizedAtUtc = finalized ?? DateTimeOffset.UtcNow.AddMinutes(-1),
        FinalizationReason = AttemptFinalizationReason.ManualSubmit,
        SkillResults = [new AttemptSkillResult { Skill = EnglishSkill.Reading, QuestionCount = 3, Score = Math.Round(correctReading * 100m / 3, 2) },
            new AttemptSkillResult { Skill = EnglishSkill.Grammar, QuestionCount = 2, Score = 0 }],
        Answers = data.Assessment.Questions.Where(q => q.Question.PrimarySkill == EnglishSkill.Reading).Take(correctReading)
            .Select(q => new AttemptAnswer { AssessmentQuestionId = q.Id, SelectedOptionId = q.Question.Options.Single(o => o.IsCorrect).Id }).ToList()
    };

    private static PracticeResponse[] Responses(Data data, bool correct = true) => data.Practice.Questions
        .Select(q => new PracticeResponse(q.Id, correct ? q.Question.Options.Single(o => o.IsCorrect).Id : null)).ToArray();

    [Fact]
    public async Task Dashboard_UsesRepresentativeAttemptWithSameSkillDataAndDeterministicTies()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var when = DateTimeOffset.UtcNow.AddMinutes(-1);
        var chosen = Graded(data, 80, 1, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), when);
        db.Attempts.AddRange(chosen, Graded(data, 80, 3, Guid.Parse("11111111-1111-1111-1111-111111111111"), when),
            Graded(data, 70, 3, finalized: when.AddMinutes(1)), Graded(data, 80, 3, finalized: when.AddMinutes(-1)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var result = await scope.ServiceProvider.GetRequiredService<IStudentLearningService>().DashboardAsync(data.Student.Id);
        var representative = Assert.Single(result.Value!.Results);
        Assert.Equal(chosen.Id, representative.AttemptId);
        Assert.Equal(80m, representative.OverallScore);
        var reading = Assert.Single(representative.Skills);
        Assert.Equal(EnglishSkill.Reading, reading.Skill);
        Assert.True(reading.IsWeak);
        var recommendation = Assert.Single(result.Value.Recommendations);
        Assert.Equal(chosen.Id, recommendation.AttemptId);
        Assert.Contains(recommendation.Contents, c => c.Id == data.Course.Id);
        Assert.Contains(recommendation.Contents, c => c.Id == data.Practice.Id);
        Assert.All(db.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Dashboard_RawWeightedScoreBelow60StillWeakWhenStoredDisplayScoreRoundsTo60()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reading = data.Assessment.Questions.Where(q => q.Question.PrimarySkill == EnglishSkill.Reading).ToArray();
        await db.AssessmentQuestions.Where(q => q.Id == reading[0].Id).ExecuteUpdateAsync(u => u.SetProperty(q => q.Points, 59999m));
        await db.AssessmentQuestions.Where(q => q.Id == reading[1].Id).ExecuteUpdateAsync(u => u.SetProperty(q => q.Points, 20000m));
        await db.AssessmentQuestions.Where(q => q.Id == reading[2].Id).ExecuteUpdateAsync(u => u.SetProperty(q => q.Points, 20001m));
        var attempt = Graded(data, 60, 1);
        attempt.SkillResults.Single(s => s.Skill == EnglishSkill.Reading).Score = 60;
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        var result = await scope.ServiceProvider.GetRequiredService<IStudentLearningService>().DashboardAsync(data.Student.Id);
        Assert.True(Assert.Single(Assert.Single(result.Value!.Results).Skills).IsWeak);
        Assert.Single(result.Value.Recommendations);
    }

    [Fact]
    public async Task Recommendation_UsesMetadataAndExcludesUnenrolledArchivedDraftHiddenOrForeignOwnerContent()
    {
        var data = await SeedAsync();
        var foreignTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Attempts.Add(Graded(data, 20, 0));
        var grammar = new Course { Title = "English Reading in name only", OwnerTeacherUserId = data.Teacher.Id,
            Status = CourseStatus.Published, Skills = [new CourseSkill { Skill = EnglishSkill.Grammar }] };
        var archived = new Course { Title = "Archived Reading", OwnerTeacherUserId = data.Teacher.Id,
            Status = CourseStatus.Archived, Skills = [new CourseSkill { Skill = EnglishSkill.Reading }] };
        var unenrolled = new Course { Title = "Unenrolled Reading", OwnerTeacherUserId = data.Teacher.Id,
            Status = CourseStatus.Published, Skills = [new CourseSkill { Skill = EnglishSkill.Reading }] };
        db.Enrollments.AddRange(new Enrollment { Course = grammar, StudentUserId = data.Student.Id }, new Enrollment { Course = archived, StudentUserId = data.Student.Id });
        db.Courses.Add(unenrolled);
        foreach (var course in new[] { grammar, archived, unenrolled })
            db.Lessons.Add(new Lesson { Title = "Available lesson", Status = LessonStatus.Published,
                Module = new Module { Course = course, Title = "Module", Visibility = ModuleVisibility.Visible } });
        var hiddenModule = new Module { CourseId = data.Course.Id, Title = "Hidden", OrderIndex = 1, Visibility = ModuleVisibility.Hidden };
        var hidden = new Practice { Module = hiddenModule, Title = "Hidden practice", OwnerTeacherUserId = data.Teacher.Id,
            Status = PracticeStatus.Published, Questions = [new PracticeQuestion { Question = Question(data.Teacher.Id, EnglishSkill.Reading, 0) }] };
        var foreign = new Practice { LessonId = data.First.Id, Title = "Foreign practice", OwnerTeacherUserId = foreignTeacher.Id,
            Status = PracticeStatus.Published, Questions = [new PracticeQuestion { Question = Question(foreignTeacher.Id, EnglishSkill.Reading, 0) }] };
        var draft = new Assessment { CourseId = data.Course.Id, OwnerTeacherUserId = data.Teacher.Id, Title = "Draft assessment", Status = AssessmentStatus.Draft,
            AssessmentType = AssessmentType.PlacementTest, DurationMinutes = 15, MaxAttempts = 1 };
        db.Practices.AddRange(hidden, foreign);
        db.Assessments.Add(draft);
        await db.SaveChangesAsync();
        var recommendation = Assert.Single((await scope.ServiceProvider.GetRequiredService<IStudentLearningService>().DashboardAsync(data.Student.Id)).Value!.Recommendations);
        Assert.DoesNotContain(recommendation.Contents, c => new[] { grammar.Id, archived.Id, unenrolled.Id, hidden.Id, foreign.Id, draft.Id }.Contains(c.Id));
        var practice = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        Assert.Null((await practice.GetSheetAsync(data.Student.Id, foreign.Id)).Value);
        Assert.Null((await practice.GetSheetAsync(data.Student.Id, hidden.Id)).Value);
    }

    [Fact]
    public async Task Dashboard_WithoutEvidenceDoesNotInventScoresOrRecommendations_AndScopesToStudent()
    {
        var data = await SeedAsync();
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var learning = scope.ServiceProvider.GetRequiredService<IStudentLearningService>();
        var dashboard = (await learning.DashboardAsync(data.Student.Id)).Value!;
        Assert.Single(dashboard.Courses);
        Assert.Empty(dashboard.Results);
        Assert.Empty(dashboard.Recommendations);
        var foreign = (await learning.DashboardAsync(other.Id)).Value!;
        Assert.Empty(foreign.Courses);
        Assert.Empty(foreign.Results);
        Assert.Equal(LearningAccessResult.Forbidden, (await learning.ActivitiesAsync(other.Id, data.Course.Id)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await learning.DashboardAsync(data.Teacher.Id)).Access);
    }

    [Fact]
    public async Task Dashboard_ScoreExactly60IsNotWeak_AndMissingMatchingMetadataShowsEmptyRecommendation()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = data.Assessment.Questions.First(q => q.Question.PrimarySkill == EnglishSkill.Reading);
        await db.AssessmentQuestions.Where(q => q.Id == first.Id).ExecuteUpdateAsync(u => u.SetProperty(q => q.Points, 3m));
        var attempt = Graded(data, 60, 1);
        attempt.SkillResults.Single(s => s.Skill == EnglishSkill.Reading).Score = 60;
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        var learning = scope.ServiceProvider.GetRequiredService<IStudentLearningService>();
        var result = (await learning.DashboardAsync(data.Student.Id)).Value!;
        Assert.False(Assert.Single(Assert.Single(result.Results).Skills).IsWeak);
        Assert.Empty(result.Recommendations);

        // Make the representative weak, then withdraw every matching piece of content.
        await db.AttemptAnswers.Where(a => a.AttemptId == attempt.Id).ExecuteDeleteAsync();
        await db.AttemptSkillResults.Where(s => s.AttemptId == attempt.Id && s.Skill == EnglishSkill.Reading)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Score, 0m));
        await db.CourseSkills.Where(s => s.CourseId == data.Course.Id).ExecuteDeleteAsync();
        await db.Practices.Where(p => p.Id == data.Practice.Id).ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, PracticeStatus.Unpublished));
        await db.Assessments.Where(a => a.Id == data.Assessment.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, AssessmentStatus.Unpublished));
        result = (await learning.DashboardAsync(data.Student.Id)).Value!;
        Assert.Empty(Assert.Single(result.Recommendations).Contents);
    }

    [Theory]
    [InlineData(AccountStatus.Disabled)]
    [InlineData(AccountStatus.Locked)]
    public async Task Services_RecheckStudentAccountStatusBeforeReadingOrWriting(AccountStatus status)
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users.Where(u => u.Id == data.Student.Id).ExecuteUpdateAsync(u => u.SetProperty(u => u.AccountStatus, status));
        var learning = scope.ServiceProvider.GetRequiredService<IStudentLearningService>();
        Assert.Equal(LearningAccessResult.Forbidden, (await learning.DashboardAsync(data.Student.Id)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await learning.AssessmentsAsync(data.Student.Id)).Access);
        var practice = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        Assert.Equal(LearningAccessResult.Forbidden, (await practice.GetSheetAsync(data.Student.Id, data.Practice.Id)).Access);
        Assert.Equal(PracticeSubmitStatus.Forbidden, (await practice.SubmitAsync(data.Student.Id, data.Practice.Id, Responses(data))).Status);
        Assert.Equal(LearningAccessResult.Forbidden, (await practice.GetResultAsync(data.Student.Id, data.Practice.Id)).Access);
    }

    [Fact]
    public async Task Continue_RechecksEffectiveLessonAndEmptyCourse_AndRetainsHistoricalCompletion()
    {
        var data = await SeedAsync();
        using var client = await ClientAsync(data.Student);
        using var first = await client.GetAsync($"/Learning/Continue/{data.Course.Id}");
        Assert.Equal($"/Learning/Lesson/{data.Second.Id}", first.Headers.Location!.ToString());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Lessons.Where(l => l.Id == data.Second.Id).ExecuteUpdateAsync(u => u.SetProperty(l => l.Status, LessonStatus.Hidden));
        using var fallback = await client.GetAsync($"/Learning/Continue/{data.Course.Id}");
        Assert.Equal($"/Learning/Lesson/{data.First.Id}", fallback.Headers.Location!.ToString());
        await db.Enrollments.Where(e => e.Id == data.Enrollment.Id).ExecuteUpdateAsync(u => u.SetProperty(e => e.CompletedAtUtc, DateTimeOffset.UtcNow));
        await db.Lessons.Where(l => l.Id == data.First.Id).ExecuteUpdateAsync(u => u.SetProperty(l => l.Status, LessonStatus.Hidden));
        using var empty = await client.GetAsync($"/Learning/Continue/{data.Course.Id}");
        Assert.Equal($"/Learning/Course/{data.Course.Id}", empty.Headers.Location!.ToString());
        var dashboard = (await scope.ServiceProvider.GetRequiredService<IStudentLearningService>().DashboardAsync(data.Student.Id)).Value!;
        Assert.Null(Assert.Single(dashboard.Courses).Progress!.Percentage);
        Assert.Equal(1, dashboard.CompletedCourses);
        Assert.Equal(data.Second.Id, await db.Enrollments.Where(e => e.Id == data.Enrollment.Id).Select(e => e.LastAccessedLessonId).SingleAsync());
    }

    [Fact]
    public async Task Activities_ListsIndependentAndLinkedAssessments_AndResumesWithdrawalWithoutNewPractice()
    {
        var data = await SeedAsync();
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var independent = new Assessment { Title = "Independent", OwnerTeacherUserId = data.Teacher.Id, DurationMinutes = 10, MaxAttempts = 1,
            Status = AssessmentStatus.Published, AssessmentType = AssessmentType.PlacementTest,
            Questions = Enumerable.Range(0, 5).Select(i => new AssessmentQuestion { OrderIndex = i, Points = 1,
                Question = Question(data.Teacher.Id, EnglishSkill.Reading, i) }).ToList() };
        db.Assessments.Add(independent);
        var attempt = new Attempt { StudentUserId = data.Student.Id, AssessmentId = data.Assessment.Id,
            Status = AttemptStatus.InProgress, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        var learning = scope.ServiceProvider.GetRequiredService<IStudentLearningService>();
        var otherList = (await learning.AssessmentsAsync(other.Id)).Value!;
        Assert.Contains(otherList, a => a.Id == independent.Id);
        Assert.DoesNotContain(otherList, a => a.Id == data.Assessment.Id);
        var activities = (await learning.ActivitiesAsync(data.Student.Id, data.Course.Id)).Value!;
        Assert.Equal(data.Practice.Id, Assert.Single(activities.Practices).Id);
        Assert.Equal(data.Assessment.Id, Assert.Single(activities.Assessments).Id);
        await db.Courses.Where(c => c.Id == data.Course.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, CourseStatus.Archived));
        await db.Assessments.Where(a => a.Id == data.Assessment.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, AssessmentStatus.Unpublished));
        activities = (await learning.ActivitiesAsync(data.Student.Id, data.Course.Id)).Value!;
        Assert.Empty(activities.Practices);
        Assert.Equal(attempt.Id, Assert.Single(activities.Assessments).ActiveAttemptId);
        await db.Attempts.Where(a => a.Id == attempt.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.DeadlineUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Empty((await learning.ActivitiesAsync(data.Student.Id, data.Course.Id)).Value!.Assessments);
    }

    [Fact]
    public async Task InvalidPaidEnrollmentCannotAccessDashboardCourseActivitiesOrPractice()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payment = Receipt(data, PaymentStatus.Failed, 100);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        await db.Enrollments.Where(e => e.Id == data.Enrollment.Id).ExecuteUpdateAsync(u => u.SetProperty(e => e.PaymentId, payment.Id));
        var learning = scope.ServiceProvider.GetRequiredService<IStudentLearningService>();
        Assert.Empty((await learning.DashboardAsync(data.Student.Id)).Value!.Courses);
        Assert.Equal(LearningAccessResult.Forbidden, (await learning.ActivitiesAsync(data.Student.Id, data.Course.Id)).Access);
        Assert.Null((await scope.ServiceProvider.GetRequiredService<IPracticeService>().GetSheetAsync(data.Student.Id, data.Practice.Id)).Value);
    }

    [Fact]
    public async Task Practice_RejectsForeignOptionsAndDuplicates_StoresLatestResultAndKeepsHistoricalFeedback()
    {
        var data = await SeedAsync();
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var practices = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        var sheet = (await practices.GetSheetAsync(data.Student.Id, data.Practice.Id)).Value!;
        Assert.DoesNotContain("IsCorrect", JsonSerializer.Serialize(sheet));
        Assert.DoesNotContain("PRIVATE_PRACTICE_EXPLANATION", JsonSerializer.Serialize(sheet));
        Assert.False(sheet.HasResult);
        var valid = Responses(data);
        var bad = valid.ToArray();
        bad[0] = bad[0] with { SelectedOptionId = valid[1].SelectedOptionId };
        Assert.Equal(PracticeSubmitStatus.InvalidAnswers, (await practices.SubmitAsync(data.Student.Id, data.Practice.Id, bad)).Status);
        Assert.Equal(PracticeSubmitStatus.InvalidAnswers, (await practices.SubmitAsync(data.Student.Id, data.Practice.Id, [valid[0], valid[0]])).Status);
        Assert.Equal(PracticeSubmitStatus.Unavailable, (await practices.SubmitAsync(other.Id, data.Practice.Id, valid)).Status);
        Assert.Equal(PracticeSubmitStatus.Saved, (await practices.SubmitAsync(data.Student.Id, data.Practice.Id, valid)).Status);
        var first = (await practices.GetResultAsync(data.Student.Id, data.Practice.Id)).Value!;
        Assert.Equal(100m, first.Score);
        Assert.True((await practices.GetSheetAsync(data.Student.Id, data.Practice.Id)).Value!.HasResult);
        Assert.All(first.Questions, q => Assert.True(q.IsCorrect));
        Assert.Null((await practices.GetResultAsync(other.Id, data.Practice.Id)).Value);
        Assert.Equal(PracticeSubmitStatus.Saved, (await practices.SubmitAsync(data.Student.Id, data.Practice.Id, Responses(data, false))).Status);
        Assert.Equal(0m, (await practices.GetResultAsync(data.Student.Id, data.Practice.Id)).Value!.Score);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.PracticeSubmissions.CountAsync(s => s.PracticeId == data.Practice.Id));
        Assert.Empty(await db.PracticeAnswers.Where(a => a.PracticeSubmission.PracticeId == data.Practice.Id).ToListAsync());
        await db.Courses.Where(c => c.Id == data.Course.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, CourseStatus.Archived));
        Assert.Equal(PracticeSubmitStatus.Unavailable, (await practices.SubmitAsync(data.Student.Id, data.Practice.Id, valid)).Status);
        Assert.NotNull((await practices.GetResultAsync(data.Student.Id, data.Practice.Id)).Value);
        Assert.Single((await scope.ServiceProvider.GetRequiredService<IStudentLearningService>().DashboardAsync(data.Student.Id)).Value!.PracticeResults);
        Assert.Empty((await scope.ServiceProvider.GetRequiredService<IStudentLearningService>().DashboardAsync(other.Id)).Value!.PracticeResults);
    }

    [Fact]
    public async Task Practice_ConcurrentRetakesKeepOneConsistentLatestSubmission()
    {
        var data = await SeedAsync();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            using var scope = factory.Services.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IPracticeService>().SubmitAsync(data.Student.Id, data.Practice.Id, Responses(data, i % 2 == 0));
            Assert.Equal(PracticeSubmitStatus.Saved, result.Status);
        }));
        using var verify = factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var submission = Assert.Single(await db.PracticeSubmissions.Include(s => s.Answers).Where(s => s.PracticeId == data.Practice.Id).ToListAsync());
        Assert.Equal(submission.Answers.Count * 100m / data.Practice.Questions.Count, submission.Score);
    }

    [Fact]
    public async Task TeacherReport_ScopesOwnerAndCountsOnlyValidEnrollmentsAndSuccessfulQuotedPayments()
    {
        var data = await SeedAsync();
        var foreign = await SeedAsync();
        var buyer = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var succeeded = Receipt(data, PaymentStatus.Succeeded, 100);
        succeeded.Order.StudentUserId = buyer.Id;
        db.Enrollments.Add(new Enrollment { CourseId = data.Course.Id, StudentUserId = buyer.Id, Payment = succeeded });
        db.Payments.AddRange(Receipt(data, PaymentStatus.Failed, 200), Receipt(data, PaymentStatus.Pending, 300),
            Receipt(data, PaymentStatus.Cancelled, 400), Receipt(foreign, PaymentStatus.Succeeded, 999));
        db.Orders.Add(new Order { CourseId = data.Course.Id, StudentUserId = data.Student.Id, Amount = 500 });
        db.LessonProgressEntries.Add(new LessonProgress { EnrollmentId = data.Enrollment.Id, LessonId = data.First.Id, IsCompleted = true });
        await db.SaveChangesAsync();
        await db.Courses.Where(c => c.Id == data.Course.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Price, 9999).SetProperty(c => c.IsPaid, true));
        var reportService = scope.ServiceProvider.GetRequiredService<ITeacherCourseReportService>();
        var report = (await reportService.GetAsync(data.Teacher.Id, data.Course.Id, new())).Value!;
        Assert.Equal(2, report.Commerce.Enrollments);
        Assert.Equal(1, report.Commerce.FreeEnrollments);
        Assert.Equal(1, report.Commerce.PaidEnrollments);
        Assert.Equal(5, report.Commerce.Orders);
        Assert.Equal(1, report.Commerce.SuccessfulOrders);
        Assert.Equal(100m, report.Commerce.SuccessfulPaymentAmount);
        Assert.Equal(50m, report.Students.Single(s => s.StudentId == data.Student.Id).Progress.Percentage);
        Assert.Equal(LearningAccessResult.Forbidden, (await reportService.GetAsync(foreign.Teacher.Id, data.Course.Id, new())).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await reportService.GetAsync(data.Student.Id, data.Course.Id, new())).Access);
    }

    [Fact]
    public async Task TeacherReport_SearchPaginationAndHistoricalCompletionStayCorrectWhenContentHidden()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var completedAt = DateTimeOffset.UtcNow.AddDays(-1);
        await db.Enrollments.Where(e => e.Id == data.Enrollment.Id).ExecuteUpdateAsync(u => u.SetProperty(e => e.CompletedAtUtc, completedAt));
        await db.Modules.Where(m => m.Id == data.Module.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.Visibility, ModuleVisibility.Hidden));
        var reports = scope.ServiceProvider.GetRequiredService<ITeacherCourseReportService>();
        var report = (await reports.GetAsync(data.Teacher.Id, data.Course.Id, new() { Search = " Student ", Completed = true, Page = int.MaxValue })).Value!;
        Assert.Equal(1, report.Query.Page);
        Assert.Equal("Student", report.Query.Search);
        Assert.Equal(1, report.Commerce.CompletedEnrollments);
        var student = Assert.Single(report.Students);
        Assert.Null(student.Progress.Percentage);
        Assert.Equal(completedAt, student.Progress.CompletedAtUtc);
        var empty = (await reports.GetAsync(data.Teacher.Id, data.Course.Id, new() { Search = "Missing name" })).Value!;
        Assert.Empty(empty.Students);
        Assert.Equal(1, empty.Commerce.Enrollments);
    }

    [Fact]
    public async Task Http_DashboardActivitiesPracticeAndTeacherReportEnforceRoleOwnerAndCsrf()
    {
        var data = await SeedAsync();
        var foreignTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var student = await ClientAsync(data.Student);
        using var dashboard = await student.GetAsync($"/Learning/Dashboard?studentId={other.Id}");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Contains("no-store", dashboard.Headers.CacheControl!.ToString());
        Assert.Contains(data.Course.Title, await dashboard.Content.ReadAsStringAsync());
        using var course = await student.GetAsync($"/Learning/Course/{data.Course.Id}");
        var html = await course.Content.ReadAsStringAsync();
        Assert.Contains($"/Practice/Take/{data.Practice.Id}", html);
        Assert.Contains($"/Assessment/Take/{data.Assessment.Id}", html);
        var token = await IdentityTestHelpers.GetTokenAsync(student, $"/Practice/Take/{data.Practice.Id}");
        using var missingCsrf = await student.PostAsync($"/Practice/Submit/{data.Practice.Id}", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        var answers = Responses(data);
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };
        for (var i = 0; i < answers.Length; i++)
        {
            form[$"Answers[{i}].PracticeQuestionId"] = answers[i].PracticeQuestionId.ToString();
            form[$"Answers[{i}].SelectedOptionId"] = answers[i].SelectedOptionId!.Value.ToString();
        }
        using var submitted = await student.PostAsync($"/Practice/Submit/{data.Practice.Id}", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        using var result = await student.GetAsync(submitted.Headers.Location);
        Assert.Contains("PRIVATE_PRACTICE_EXPLANATION", await result.Content.ReadAsStringAsync());
        using var outsider = await ClientAsync(other);
        using var deniedSheet = await outsider.GetAsync($"/Practice/Take/{data.Practice.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deniedSheet.StatusCode);
        using var deniedResult = await outsider.GetAsync($"/Practice/Result/{data.Practice.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deniedResult.StatusCode);
        using var deniedContinue = await outsider.GetAsync($"/Learning/Continue/{data.Course.Id}");
        Assert.Contains("/Account/AccessDenied", deniedContinue.Headers.Location!.ToString());
        using var teacher = await ClientAsync(data.Teacher);
        using var report = await teacher.GetAsync($"/TeacherCourses/Students/{data.Course.Id}");
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        Assert.Contains("no-store", report.Headers.CacheControl!.ToString());
        Assert.DoesNotContain(data.Student.Email!, await report.Content.ReadAsStringAsync());
        using var foreignClient = await ClientAsync(foreignTeacher);
        using var deniedReport = await foreignClient.GetAsync($"/TeacherCourses/Students/{data.Course.Id}?Search=Student");
        Assert.Contains("/Account/AccessDenied", deniedReport.Headers.Location!.ToString());
        using var deniedDashboard = await teacher.GetAsync("/Learning/Dashboard");
        Assert.Contains("/Account/AccessDenied", deniedDashboard.Headers.Location!.ToString());
        using var guest = IdentityTestHelpers.CreateClient(factory);
        using var anonymous = await guest.GetAsync("/Learning/Dashboard");
        Assert.Contains("/Account/Login", anonymous.Headers.Location!.ToString());
    }

    private static Payment Receipt(Data data, PaymentStatus status, decimal amount) => new()
    {
        Order = new Order { CourseId = data.Course.Id, StudentUserId = data.Student.Id, Amount = amount, Currency = "VND" },
        Amount = amount, Status = status, Provider = "Test", ProviderTransactionId = Guid.NewGuid().ToString("N")
    };

    private async Task<HttpClient> ClientAsync(ApplicationUser user)
    {
        var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    private sealed record Data(ApplicationUser Teacher, ApplicationUser Student, Course Course, Module Module, Lesson First,
        Lesson Second, Enrollment Enrollment, Practice Practice, Assessment Assessment);
}
