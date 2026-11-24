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
