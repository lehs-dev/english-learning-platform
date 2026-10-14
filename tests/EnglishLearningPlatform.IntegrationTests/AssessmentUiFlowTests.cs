using System.Net;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class AssessmentUiFlowTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task GetOverview_DoesNotConsumeAttempts_AndStartRequiresStudentEnrollmentAndCsrf()
    {
        var data = await SeedAsync();
        using var guest = IdentityTestHelpers.CreateClient(factory);
        using var anonymous = await guest.GetAsync($"/Assessment/Take/{data.Assessment.Id}");
        Assert.Contains("/Account/Login", anonymous.Headers.Location!.ToString());
        using var client = await StudentClient(data.Student);
        using var overview = await client.GetAsync($"/Assessment/Take/{data.Assessment.Id}");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        var html = await overview.Content.ReadAsStringAsync();
        Assert.Contains("5 câu", html);
        Assert.DoesNotContain("QUESTION_PRIVATE_EXPLANATION", html);
        using var refresh = await client.GetAsync($"/Assessment/Take/{data.Assessment.Id}");
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        using var missingToken = await client.PostAsync($"/Assessment/Start/{data.Assessment.Id}", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Attempts.CountAsync(a => a.AssessmentId == data.Assessment.Id));

        var foreign = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var foreignClient = await StudentClient(foreign);
        using var noEnrollment = await foreignClient.GetAsync($"/Assessment/Take/{data.Assessment.Id}");
        Assert.Equal(HttpStatusCode.NotFound, noEnrollment.StatusCode);
        var foreignToken = await IdentityTestHelpers.GetTokenAsync(foreignClient, "/Profile");
        using var deniedStart = await Post(foreignClient, $"/Assessment/Start/{data.Assessment.Id}", foreignToken);
        Assert.Contains("/Account/AccessDenied", deniedStart.Headers.Location!.ToString());
        using var teacher = await StudentClient(data.Teacher);
        using var teacherAttempt = await teacher.GetAsync($"/Assessment/Take/{data.Assessment.Id}");
        Assert.Contains("/Account/AccessDenied", teacherAttempt.Headers.Location!.ToString());
    }

    [Fact]
    public async Task SubmitPassedFinal_ShowsSkillScoresAndCompletion_PreservesHistoryAfterLessonAddedAndReplay()
    {
        var data = await SeedAsync();
        using var client = await StudentClient(data.Student);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Assessment/Take/{data.Assessment.Id}");
        var attemptId = await Start(client, data, token);
        using var submitted = await Post(client, $"/Assessment/Submit/{attemptId}", token, Answers(data, correctCount: 4));
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        Assert.Equal($"/Assessment/Result/{attemptId}", submitted.Headers.Location!.ToString());
        using var result = await client.GetAsync(submitted.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var resultHtml = await result.Content.ReadAsStringAsync();
        Assert.Contains("80", resultHtml);
        Assert.Contains("Grammar", resultHtml);
        Assert.Contains("Vocabulary", resultHtml);
        Assert.Contains("assessment-passed", resultHtml);
        Assert.DoesNotContain("QUESTION_PRIVATE_EXPLANATION", resultHtml);
        DateTimeOffset completedAt;
        DateTimeOffset finalizedAt;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            completedAt = (await db.Enrollments.SingleAsync(e => e.StudentUserId == data.Student.Id && e.CourseId == data.Course.Id)).CompletedAtUtc!.Value;
            finalizedAt = (await db.Attempts.SingleAsync(a => a.Id == attemptId)).FinalizedAtUtc!.Value;
            Assert.Equal(2, await db.AttemptSkillResults.CountAsync(s => s.AttemptId == attemptId));
            db.Lessons.Add(new Lesson { ModuleId = data.Module.Id, Title = "New lesson after graduation", OrderIndex = 1, Status = LessonStatus.Published });
            await db.SaveChangesAsync();
        }
        using var replay = await Post(client, $"/Assessment/Submit/{attemptId}", token, Answers(data, correctCount: 0));
        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        using var course = await client.GetAsync($"/Learning/Course/{data.Course.Id}");
        var courseHtml = await course.Content.ReadAsStringAsync();
        Assert.Contains("course-completed", courseHtml);
        Assert.Contains("assessment-details", courseHtml);
        Assert.Contains("50%", courseHtml);
        using var overview = await client.GetAsync($"/Assessment/Take/{data.Assessment.Id}");
        Assert.Contains($"/Assessment/Result/{attemptId}", await overview.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var attempt = await db.Attempts.SingleAsync(a => a.Id == attemptId);
            Assert.Equal(80m, attempt.OverallScore);
            Assert.Equal(finalizedAt, attempt.FinalizedAtUtc);
            Assert.Equal(completedAt, (await db.Enrollments.SingleAsync(e => e.StudentUserId == data.Student.Id && e.CourseId == data.Course.Id)).CompletedAtUtc);
            Assert.Equal(2, await db.AttemptSkillResults.CountAsync(s => s.AttemptId == attemptId));
        }
    }

    [Fact]
    public async Task SaveResume_RejectsMalformedForeignAndDuplicateAnswersBeforeWriting_AndEncodesContent()
    {
        var data = await SeedAsync();
        using var client = await StudentClient(data.Student);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Assessment/Take/{data.Assessment.Id}");
        var attemptId = await Start(client, data, token);
        using var taking = await client.GetAsync($"/Assessment/Take/{data.Assessment.Id}?attemptId={attemptId}");
        var html = await taking.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("IsCorrect", html);
        Assert.DoesNotContain("QUESTION_PRIVATE_EXPLANATION", html);
        Assert.Contains("data-assessment-clock", html);
        var invalid = Answers(data, 5);
        invalid["Answers[4].SelectedOptionId"] = data.Assessment.Questions.First().Question.Options.First().Id.ToString();
        using var invalidPost = await Post(client, $"/Assessment/Save/{attemptId}", token, invalid);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPost.StatusCode);
        var duplicate = Answers(data, 5);
        duplicate["Answers[4].AssessmentQuestionId"] = duplicate["Answers[0].AssessmentQuestionId"];
        using var duplicatePost = await Post(client, $"/Assessment/Submit/{attemptId}", token, duplicate);
        Assert.Equal(HttpStatusCode.BadRequest, duplicatePost.StatusCode);
        using var invalidBinding = await Post(client, $"/Assessment/Save/{attemptId}", token, new() { ["Answers[0].AssessmentQuestionId"] = "invalid-guid" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidBinding.StatusCode);
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().AttemptAnswers.CountAsync(a => a.AttemptId == attemptId));
        using var saved = await Post(client, $"/Assessment/Save/{attemptId}", token, Answers(data, 5));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        using var resumed = await Post(client, $"/Assessment/Start/{data.Assessment.Id}", token);
        Assert.Contains(attemptId.ToString(), resumed.Headers.Location!.ToString());
        using var refreshed = await client.GetAsync(resumed.Headers.Location);
        var resumedHtml = await refreshed.Content.ReadAsStringAsync();
        Assert.Equal(5, Regex.Matches(resumedHtml, "checked=\"checked\"").Count);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Attempts.CountAsync(a => a.AssessmentId == data.Assessment.Id));
            Assert.Equal(5, await db.AttemptAnswers.CountAsync(a => a.AttemptId == attemptId));
        }
    }

    [Fact]
    public async Task ExpiredGetNeverGrades_PostStartFinalizesSavedAnswersWithoutCreatingNewAttempt()
    {
        var data = await SeedAsync();
        using var client = await StudentClient(data.Student);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Assessment/Take/{data.Assessment.Id}");
        var attemptId = await Start(client, data, token);
        using var saved = await Post(client, $"/Assessment/Save/{attemptId}", token, Answers(data, 4));
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Attempts.Where(a => a.Id == attemptId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.DeadlineUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
        using var get = await client.GetAsync($"/Assessment/Take/{data.Assessment.Id}?attemptId={attemptId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(AttemptStatus.InProgress, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Attempts.SingleAsync(a => a.Id == attemptId)).Status);
        using var finalize = await Post(client, $"/Assessment/Start/{data.Assessment.Id}", token);
        Assert.Equal($"/Assessment/Result/{attemptId}", finalize.Headers.Location!.ToString());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var attempt = await db.Attempts.SingleAsync(a => a.Id == attemptId);
            Assert.Equal(80m, attempt.OverallScore);
            Assert.Equal(AttemptFinalizationReason.DeadlineElapsed, attempt.FinalizationReason);
            Assert.Equal(1, await db.Attempts.CountAsync(a => a.AssessmentId == data.Assessment.Id));
        }
    }

    [Fact]
    public async Task ForeignAttempt_ResultReadAndSaveSubmitDenied_AndResultSurvivesWithdrawal()
    {
        var data = await SeedAsync();
        using var client = await StudentClient(data.Student);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Assessment/Take/{data.Assessment.Id}");
        var attemptId = await Start(client, data, token);
        using var graded = await Post(client, $"/Assessment/Submit/{attemptId}", token, Answers(data, 5));
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var foreignClient = await StudentClient(other);
        var otherToken = await IdentityTestHelpers.GetTokenAsync(foreignClient, "/Profile");
        using var foreignResult = await foreignClient.GetAsync($"/Assessment/Result/{attemptId}");
        Assert.Equal(HttpStatusCode.NotFound, foreignResult.StatusCode);
        using var foreignTake = await foreignClient.GetAsync($"/Assessment/Take/{data.Assessment.Id}?attemptId={attemptId}");
        Assert.Equal(HttpStatusCode.NotFound, foreignTake.StatusCode);
        foreach (var action in new[] { "Save", "Submit" })
        {
            using var csrf = await client.PostAsync($"/Assessment/{action}/{attemptId}", new FormUrlEncodedContent(Answers(data, 0)));
            Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
            using var foreignMutation = await Post(foreignClient, $"/Assessment/{action}/{attemptId}", otherToken, Answers(data, 0));
            Assert.Equal(HttpStatusCode.NotFound, foreignMutation.StatusCode);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Assessments.Where(a => a.Id == data.Assessment.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AssessmentStatus.Draft));
            await db.Courses.Where(c => c.Id == data.Course.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CourseStatus.Archived));
            await db.LessonProgressEntries.Where(p => p.Enrollment.StudentUserId == data.Student.Id && p.Enrollment.CourseId == data.Course.Id).ExecuteDeleteAsync();
            await db.Enrollments.Where(e => e.StudentUserId == data.Student.Id && e.CourseId == data.Course.Id).ExecuteDeleteAsync();
        }
        using var historical = await client.GetAsync($"/Assessment/Result/{attemptId}");
        Assert.Equal(HttpStatusCode.OK, historical.StatusCode);
        Assert.Contains("100", await historical.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PlacementResultWithoutPassingScore_DoesNotLabelStudentFailed_AndExhaustedAttemptsRetainResultLink()
    {
        var data = await SeedAsync(passingScore: null, maxAttempts: 1);
        using var client = await StudentClient(data.Student);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Assessment/Take/{data.Assessment.Id}");
        var attemptId = await Start(client, data, token);
        using var graded = await Post(client, $"/Assessment/Submit/{attemptId}", token, Answers(data, 0));
        using var result = await client.GetAsync(graded.Headers.Location);
        Assert.DoesNotContain("assessment-failed", await result.Content.ReadAsStringAsync());
        using var second = await Post(client, $"/Assessment/Start/{data.Assessment.Id}", token);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        using var overview = await client.GetAsync(second.Headers.Location);
        Assert.Contains($"/Assessment/Result/{attemptId}", await overview.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Attempts.CountAsync(a => a.AssessmentId == data.Assessment.Id));
    }

    [Fact]
    public async Task ListeningAudio_IsAvailableOnlyToEnrolledAttemptOwner_AndResumeKeepsDeadline()
    {
        var data = await SeedAsync();
        const string audioUrl = "https://example.com/listening-regression.mp3";
        var question = data.Assessment.Questions.OrderBy(q => q.OrderIndex).First();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.Questions.SingleAsync(q => q.Id == question.QuestionId);
            entity.PrimarySkill = EnglishSkill.Listening;
            entity.Stimulus = new Stimulus { OwnerTeacherUserId = data.Teacher.Id,
                StimulusType = StimulusType.Audio, ResourceUrl = audioUrl, ContentText = "Listen and choose." };
            db.Stimuli.Add(entity.Stimulus);
            await db.SaveChangesAsync();
        }
        using var client = await StudentClient(data.Student);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Assessment/Take/{data.Assessment.Id}");
        var attemptId = await Start(client, data, token);
        DateTimeOffset deadline;
        using (var scope = factory.Services.CreateScope())
            deadline = (await scope.ServiceProvider.GetRequiredService<IAssessmentQueryService>()
                .GetTakingSheetAsync(data.Student.Id, attemptId))!.DeadlineUtc;
        using var saved = await Post(client, $"/Assessment/Save/{attemptId}", token, Answers(data, 4));
        using var resumed = await Post(client, $"/Assessment/Start/{data.Assessment.Id}", token);
        using var page = await client.GetAsync(resumed.Headers.Location);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("<audio", html);
        Assert.Contains(audioUrl, html);
        Assert.DoesNotContain("QUESTION_PRIVATE_EXPLANATION", html);
        using (var scope = factory.Services.CreateScope())
        {
            var sheet = await scope.ServiceProvider.GetRequiredService<IAssessmentQueryService>()
                .GetTakingSheetAsync(data.Student.Id, attemptId);
            Assert.Equal(deadline, sheet!.DeadlineUtc);
            Assert.All(sheet.Questions, q => Assert.NotNull(q.SelectedOptionId));
        }
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var otherClient = await StudentClient(other);
        using var denied = await otherClient.GetAsync($"/Assessment/Take/{data.Assessment.Id}?attemptId={attemptId}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain(audioUrl, await denied.Content.ReadAsStringAsync());
    }

    private async Task<HttpClient> StudentClient(ApplicationUser user)
    {
        var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string path, string token, Dictionary<string, string>? input = null)
    {
        input ??= [];
        input["__RequestVerificationToken"] = token;
        return client.PostAsync(path, new FormUrlEncodedContent(input));
    }

    private async Task<Guid> Start(HttpClient client, Fixture data, string token)
    {
        using var started = await Post(client, $"/Assessment/Start/{data.Assessment.Id}", token);
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        var match = Regex.Match(started.Headers.Location!.ToString(), "attemptId=([0-9a-f-]+)");
        Assert.True(match.Success);
        return Guid.Parse(match.Groups[1].Value);
    }

    private static Dictionary<string, string> Answers(Fixture data, int correctCount)
    {
        var answers = new Dictionary<string, string>();
        var questions = data.Assessment.Questions.OrderBy(q => q.OrderIndex).ToArray();
        for (var i = 0; i < questions.Length; i++)
        {
            answers[$"Answers[{i}].AssessmentQuestionId"] = questions[i].Id.ToString();
            answers[$"Answers[{i}].SelectedOptionId"] = questions[i].Question.Options.Single(o => o.IsCorrect == (i < correctCount)).Id.ToString();
        }
        return answers;
    }

    private async Task<Fixture> SeedAsync(decimal? passingScore = 80m, int maxAttempts = 3)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var module = new Module
        {
            Title = "UI module",
            Lessons = [new Lesson { Title = "UI lesson", Status = LessonStatus.Published,
                Resources = [new LessonResource { ResourceType = LessonResourceType.Text, ContentText = "Content" }] }]
        };
        var course = new Course { Title = "UI final course", OwnerTeacherUserId = teacher.Id, Status = CourseStatus.Published, Modules = [module] };
        var assessment = new Assessment
        {
            Title = "Final via browser", OwnerTeacherUserId = teacher.Id, Course = course,
            Status = AssessmentStatus.Published, DurationMinutes = 20, MaxAttempts = maxAttempts,
            PassingScore = passingScore, AssessmentType = passingScore.HasValue ? AssessmentType.PracticeExam : AssessmentType.PlacementTest,
            Questions = Enumerable.Range(0, 5).Select(i => new AssessmentQuestion
            {
                OrderIndex = i, Points = 1m,
                Question = new Question
                {
                    OwnerTeacherUserId = teacher.Id, Status = QuestionStatus.Published,
                    Content = i == 0 ? "<script>alert('xss')</script> Encoded question" : $"Question {i + 1}",
                    Explanation = "QUESTION_PRIVATE_EXPLANATION",
                    PrimarySkill = i < 3 ? EnglishSkill.Grammar : EnglishSkill.Vocabulary,
                    Options = [new QuestionOption { Content = "First choice", IsCorrect = true },
                        new QuestionOption { Content = "Other choice", OrderIndex = 1 }]
                }
            }).ToList()
        };
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();
        course.FinalAssessmentId = passingScore.HasValue ? assessment.Id : null;
        var enrollment = new Enrollment { StudentUserId = student.Id, CourseId = course.Id };
        db.Enrollments.Add(enrollment);
        db.LessonProgressEntries.Add(new LessonProgress { Enrollment = enrollment, LessonId = module.Lessons.Single().Id, IsCompleted = true });
        await db.SaveChangesAsync();
        return new Fixture(student, teacher, course, module, assessment);
    }

    private sealed record Fixture(ApplicationUser Student, ApplicationUser Teacher, Course Course, Module Module, Assessment Assessment);
}
