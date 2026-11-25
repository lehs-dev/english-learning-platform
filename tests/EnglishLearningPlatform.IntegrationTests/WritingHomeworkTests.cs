using System.Net;
using EnglishLearningPlatform.Application.Gamification;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class WritingHomeworkTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task CourseOwnerAssignsAndReviews_EnrolledStudentSubmitsOnceAndReceivesPrivateFeedback()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var outsideTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var classmate = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Writing course", Status = CourseStatus.Published };
        db.Enrollments.AddRange(
            new Enrollment { Course = course, StudentUserId = student.Id },
            new Enrollment { Course = course, StudentUserId = classmate.Id });
        await db.SaveChangesAsync();

        using var teacherClient = await LoginAsync(teacher);
        using var outsiderClient = await LoginAsync(outsideTeacher);
        using var studentClient = await LoginAsync(student);
        using var classmateClient = await LoginAsync(classmate);

        var createPath = $"/Homework/Create/{course.Id}";
        var createToken = await IdentityTestHelpers.GetTokenAsync(teacherClient, createPath);
        const string prompt = "Describe your neighborhood in two paragraphs.";
        using var assigned = await PostAsync(teacherClient, createPath, createToken, [new("Title", "My neighborhood"), new("Prompt", prompt)]);
        Assert.Equal(HttpStatusCode.Redirect, assigned.StatusCode);
        var assignment = await db.WritingAssignments.AsNoTracking().SingleAsync(row => row.CourseId == course.Id);
        var detailsPath = $"/Homework/Details/{assignment.Id}";
        Assert.EndsWith(detailsPath, assigned.Headers.Location!.OriginalString);
        Assert.Contains(prompt, await studentClient.GetStringAsync(detailsPath));
        using var denied = await outsiderClient.GetAsync(detailsPath);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("/Account/AccessDenied", denied.Headers.Location!.OriginalString);

        var studentToken = await IdentityTestHelpers.GetTokenAsync(studentClient, detailsPath);
        var submitPath = $"/Homework/Submit/{assignment.Id}";
        const string response = "My neighborhood has a small library and a busy market.";
        using var submitted = await PostAsync(studentClient, submitPath, studentToken, [new("response", response)]);
        using var repeated = await PostAsync(studentClient, submitPath, studentToken, [new("response", "A different response.")]);
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, repeated.StatusCode);
        var submission = await db.WritingSubmissions.AsNoTracking().SingleAsync(row => row.AssignmentId == assignment.Id);
        Assert.Equal(student.Id, submission.StudentUserId);
        Assert.Equal(response, submission.Response);
        var activity = await db.StudentActivityDays.AsNoTracking().SingleAsync(day => day.StudentUserId == student.Id);
        Assert.Equal(GamificationPolicy.LocalDate(submission.CreatedAtUtc), activity.ActivityDate);

        var reviewToken = await IdentityTestHelpers.GetTokenAsync(teacherClient, detailsPath);
        var reviewPath = $"/Homework/Review/{assignment.Id}?submissionId={submission.Id}";
        const string feedback = "Add one example to support the second paragraph.";
        using var reviewed = await PostAsync(teacherClient, reviewPath, reviewToken, [new("Feedback", feedback), new("Score", "86")]);
        Assert.Equal(HttpStatusCode.Redirect, reviewed.StatusCode);
        var savedReview = await db.WritingSubmissions.AsNoTracking().SingleAsync(row => row.Id == submission.Id);
        Assert.Equal(feedback, savedReview.Feedback);
        Assert.Equal(86, savedReview.Score);
        Assert.Contains(feedback, await studentClient.GetStringAsync(detailsPath));
        var classmatePage = await classmateClient.GetStringAsync(detailsPath);
        Assert.DoesNotContain(response, classmatePage);
        Assert.DoesNotContain(feedback, classmatePage);
    }

    [Fact]
    public async Task DraftSurvivesRefresh_RejectsStaleSave_AndBecomesPrivateFinalSubmission()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var classmate = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Draft course", Status = CourseStatus.Published };
        var assignment = new WritingAssignment { Course = course, Title = "Writing draft", Prompt = "Write a short dialogue." };
        var closing = new WritingAssignment { Course = course, Title = "Closing task", Prompt = "Write a short reply." };
        db.WritingAssignments.AddRange(assignment, closing);
        db.Enrollments.AddRange(
            new Enrollment { Course = course, StudentUserId = student.Id },
            new Enrollment { Course = course, StudentUserId = classmate.Id });
        await db.SaveChangesAsync();

        using var studentClient = await LoginAsync(student);
        using var teacherClient = await LoginAsync(teacher);
        using var classmateClient = await LoginAsync(classmate);
        var details = $"/Homework/Details/{assignment.Id}";
        var save = $"/Homework/SaveDraft/{assignment.Id}";
        var token = await IdentityTestHelpers.GetTokenAsync(studentClient, details);
        Assert.Contains($"formaction=\"{save}\"", await studentClient.GetStringAsync(details));
        using var missingToken = await studentClient.PostAsync(save, new FormUrlEncodedContent([new("response", "Draft")]));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);

        const string first = "An unfinished first version.";
        using var initial = await PostAsync(studentClient, save, token, [new("response", first)]);
        Assert.Equal(HttpStatusCode.Redirect, initial.StatusCode);
        using var retry = await PostAsync(studentClient, save, token, [new("response", first)]);
        Assert.Equal(HttpStatusCode.Redirect, retry.StatusCode);
        Assert.Contains(first, await studentClient.GetStringAsync(details));
        Assert.DoesNotContain(first, await teacherClient.GetStringAsync(details));
        Assert.DoesNotContain(first, await classmateClient.GetStringAsync(details));
        Assert.False(await db.WritingSubmissions.AnyAsync(row => row.AssignmentId == assignment.Id));
        Assert.False(await db.StudentActivityDays.AnyAsync(day => day.StudentUserId == student.Id));

        var version = await db.WritingSubmissionDrafts.Where(row => row.AssignmentId == assignment.Id)
            .Select(row => row.Version).SingleAsync();
        const string firstEdit = "My revisions in one tab.";
        const string secondEdit = "My revisions in a second tab.";
        var saves = await Task.WhenAll(
            PostAsync(studentClient, save, token, [new("response", firstEdit), new("draftVersion", version.ToString())]),
            PostAsync(studentClient, save, token, [new("response", secondEdit), new("draftVersion", version.ToString())]));
        using var winner = Assert.Single(saves, item => item.StatusCode == HttpStatusCode.Redirect);
        using var conflict = Assert.Single(saves, item => item.StatusCode == HttpStatusCode.Conflict);
        var conflictPage = await conflict.Content.ReadAsStringAsync();
        Assert.Contains(firstEdit, conflictPage);
        Assert.Contains(secondEdit, conflictPage);

        const string final = "The student completed the dialogue.";
        using var submitted = await PostAsync(studentClient, $"/Homework/Submit/{assignment.Id}", token, [new("response", final)]);
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        using var tooLate = await PostAsync(studentClient, save, token, [new("response", "A draft after submitting.")]);
        Assert.Equal(HttpStatusCode.Redirect, tooLate.StatusCode);
        Assert.False(await db.WritingSubmissionDrafts.AnyAsync(row => row.AssignmentId == assignment.Id));
        var submission = await db.WritingSubmissions.AsNoTracking().SingleAsync(row => row.AssignmentId == assignment.Id);
        Assert.Equal(final, submission.Response);
        Assert.Single(await db.StudentActivityDays.Where(day => day.StudentUserId == student.Id).ToListAsync());

        var closingSave = $"/Homework/SaveDraft/{closing.Id}";
        const string held = "This draft remains readable when closed.";
        using var closingSaved = await PostAsync(studentClient, closingSave, token, [new("response", held)]);
        Assert.Equal(HttpStatusCode.Redirect, closingSaved.StatusCode);
        await db.Courses.Where(row => row.Id == course.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, CourseStatus.Archived));
        var closedPage = await studentClient.GetStringAsync($"/Homework/Details/{closing.Id}");
        Assert.Contains(held, closedPage);
        Assert.DoesNotContain("writing-response", closedPage);
        using var closedDraft = await PostAsync(studentClient, closingSave, token, [new("response", "Should not replace archival copy.")]);
        Assert.Equal(HttpStatusCode.Redirect, closedDraft.StatusCode);
        Assert.Equal(held, await db.WritingSubmissionDrafts.Where(row => row.AssignmentId == closing.Id).Select(row => row.Response).SingleAsync());

        var archivedReview = $"/Homework/Review/{assignment.Id}?submissionId={submission.Id}";
        var teacherToken = await IdentityTestHelpers.GetTokenAsync(teacherClient, details);
        using var closedReview = await PostAsync(teacherClient, archivedReview, teacherToken, [new("Feedback", "Should remain read-only.")]);
        Assert.Equal(HttpStatusCode.Redirect, closedReview.StatusCode);
        Assert.Null(await db.WritingSubmissions.Where(row => row.Id == submission.Id).Select(row => row.Feedback).SingleAsync());
    }

    private async Task<HttpClient> LoginAsync(ApplicationUser user)
    {
        var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string token,
        IEnumerable<KeyValuePair<string, string>> fields)
    {
        using var form = new FormUrlEncodedContent(fields.Append(new("__RequestVerificationToken", token)));
        return await client.PostAsync(path, form);
    }
}
