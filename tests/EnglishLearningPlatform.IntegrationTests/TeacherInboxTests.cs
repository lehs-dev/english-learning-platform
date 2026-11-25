using System.Net;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class TeacherInboxTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task InboxShowsOwnedActionableWorkAcrossCourses_AndFiltersAndPaginates()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var otherTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var classmate = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);

        var firstEnrollment = Enroll(teacher, student, "Course with questions", CourseStatus.Unpublished);
        var secondEnrollment = Enroll(teacher, classmate, "Course with writing", CourseStatus.Published);
        var emptyEnrollment = Enroll(teacher, student, "Course with nothing pending", CourseStatus.Published);
        var archivedEnrollment = Enroll(teacher, student, "Archived course", CourseStatus.Archived);
        var foreignEnrollment = Enroll(otherTeacher, student, "Foreign course", CourseStatus.Published);
        var assignment = Assignment(secondEnrollment.Course, "Needs a review", student, classmate);
        var reviewed = Assignment(emptyEnrollment.Course, "Already reviewed", student);
        var reviewedSubmission = reviewed.Submissions.Single();
        reviewedSubmission.Feedback = "Saved feedback";
        reviewedSubmission.ReviewedAtUtc = DateTimeOffset.UtcNow;

        var firstQuestion = Ask(firstEnrollment, "Oldest pending question", 0);
        var latestQuestion = Ask(firstEnrollment, "Last question on page two", 25);
        var answeredQuestion = Ask(firstEnrollment, "Teacher already answered", 0);
        answeredQuestion.Messages.Add(new CourseDiscussionMessage
        {
            AuthorUserId = teacher.Id, WrittenByTeacher = true, SequenceNumber = 2, Body = "An answer.",
            CreatedAtUtc = answeredQuestion.Messages.Single().CreatedAtUtc.AddDays(1)
        });
        var moreQuestions = Enumerable.Range(1, 19).Select(index => Ask(firstEnrollment, $"Extra question {index}", index));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Enrollments.AddRange(firstEnrollment, secondEnrollment, emptyEnrollment, archivedEnrollment, foreignEnrollment);
            db.Enrollments.Add(new Enrollment { Course = secondEnrollment.Course, StudentUserId = student.Id });
            db.WritingAssignments.AddRange(assignment, reviewed,
                Assignment(archivedEnrollment.Course, "Archived writing", student),
                Assignment(foreignEnrollment.Course, "Foreign writing", student));
            db.CourseDiscussionThreads.AddRange(firstQuestion, latestQuestion, answeredQuestion,
                Ask(archivedEnrollment, "Archived question", 0), Ask(foreignEnrollment, "Foreign question", 0));
            db.CourseDiscussionThreads.AddRange(moreQuestions);
            await db.SaveChangesAsync();
        }

        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, teacher);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var overview = await client.GetAsync("/TeacherInbox");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        Assert.True(overview.Headers.CacheControl?.NoStore);
        var html = WebUtility.HtmlDecode(await overview.Content.ReadAsStringAsync());
        Assert.Contains($"/Homework/Details/{assignment.Id}", html);
        Assert.Contains("2 bài nộp", html);
        Assert.Contains($"/CourseDiscussion/Details/{firstQuestion.Id}", html);
        Assert.DoesNotContain("Private contents of a submission.", html);
        Assert.DoesNotContain("Private contents of a question.", html);
        Assert.DoesNotContain("Already reviewed", html);
        Assert.DoesNotContain("Teacher already answered", html);
        Assert.DoesNotContain("Archived course", html);
        Assert.DoesNotContain("Foreign course", html);

        var secondPage = WebUtility.HtmlDecode(await client.GetStringAsync(
            $"/TeacherInbox?kind=Discussion&courseId={firstEnrollment.Course.Id}&page=2"));
        Assert.Contains($"/CourseDiscussion/Details/{latestQuestion.Id}", secondPage);
        Assert.DoesNotContain($"/CourseDiscussion/Details/{firstQuestion.Id}", secondPage);
        Assert.DoesNotContain($"/Homework/Details/{assignment.Id}", secondPage);
        Assert.Contains("Trang 2/2", secondPage);
        var emptyPage = WebUtility.HtmlDecode(await client.GetStringAsync($"/TeacherInbox?courseId={emptyEnrollment.Course.Id}"));
        Assert.Contains("Không có việc nào đang chờ", emptyPage);

        using var foreignFilter = await client.GetAsync($"/TeacherInbox?courseId={foreignEnrollment.Course.Id}");
        Assert.Equal(HttpStatusCode.NotFound, foreignFilter.StatusCode);
        using var studentClient = IdentityTestHelpers.CreateClient(factory);
        using var studentLogin = await IdentityTestHelpers.LoginAsync(studentClient, student);
        Assert.Equal(HttpStatusCode.Redirect, studentLogin.StatusCode);
        using var denied = await studentClient.GetAsync("/TeacherInbox");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("/Account/AccessDenied", denied.Headers.Location!.OriginalString);
    }

    private static Enrollment Enroll(ApplicationUser teacher, ApplicationUser student, string title, CourseStatus status) =>
        new()
        {
            StudentUserId = student.Id,
            Course = new Course { OwnerTeacherUserId = teacher.Id, Title = title, Status = status }
        };

    private static WritingAssignment Assignment(Course course, string title, params ApplicationUser[] students) =>
        new()
        {
            Course = course, Title = title, Prompt = "Write a short paragraph.",
            Submissions = students.Select(student => new WritingSubmission
            {
                StudentUserId = student.Id, Response = "Private contents of a submission."
            }).ToList()
        };

    private static CourseDiscussionThread Ask(Enrollment enrollment, string subject, int day) =>
        new()
        {
            Subject = subject,
            Enrollment = enrollment,
            Messages =
            [
                new CourseDiscussionMessage
                {
                    AuthorUserId = enrollment.StudentUserId, SequenceNumber = 1,
                    Body = "Private contents of a question.",
                    CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(day)
                }
            ]
        };
}
