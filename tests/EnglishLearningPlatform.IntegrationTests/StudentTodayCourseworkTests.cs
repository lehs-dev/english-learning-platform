using System.Net;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class StudentTodayCourseworkTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task TodayShowsUpcomingWritingAndOwnRecentFeedbackAcrossActiveEnrollments()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var classmate = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var active = Course("Course open", CourseStatus.Published);
        var closedToNewStudents = Course("Course still available to enrolled students", CourseStatus.Unpublished);
        var archived = Course("Course archived", CourseStatus.Archived);
        var foreign = Course("Course without enrollment", CourseStatus.Published);
        var mine = Enroll(active, student.Id);
        var theirs = Enroll(active, classmate.Id);
        db.Courses.AddRange(active, closedToNewStudents, archived, foreign);
        db.Enrollments.AddRange(mine, theirs, Enroll(closedToNewStudents, student.Id), Enroll(archived, student.Id));

        var urgent = Assignment(active, "Writing due soon", now.AddHours(1));
        var later = Assignment(closedToNewStudents, "Writing due later", now.AddDays(2));
        var undated = Assignment(active, "Writing without deadline");
        var expires = Assignment(active, "Expired writing", now.AddHours(-1));
        var awaiting = Assignment(active, "Writing already submitted");
        var reviewed = Assignment(active, "Writing reviewed recently");
        var old = Assignment(active, "Writing reviewed previously");
        var archiveTask = Assignment(archived, "Archived coursework");
        var outsiderTask = Assignment(foreign, "Coursework without enrollment");
        urgent.Submissions.Add(Submission(classmate.Id, now.AddDays(-1)));
        awaiting.Submissions.Add(Submission(student.Id));
        reviewed.Submissions.Add(Submission(student.Id, now.AddDays(-2)));
        old.Submissions.Add(Submission(student.Id, now.AddDays(-15)));
        db.WritingAssignments.AddRange(urgent, later, undated, expires, awaiting, reviewed, old, archiveTask, outsiderTask);

        var newestReply = Thread(mine, "My private question with reply");
        var classmateReply = Thread(theirs, "Another private question with reply");
        var alreadyResponded = Thread(mine, "My latest reply is already sent");
        alreadyResponded.Messages.Add(Message(student.Id, false, 3, now.AddHours(-1)));
        db.CourseDiscussionThreads.AddRange(newestReply, classmateReply, alreadyResponded);
        await db.SaveChangesAsync();

        var learning = services.GetRequiredService<IStudentLearningService>();
        var outcome = await learning.TodayAsync(student.Id);
        Assert.Equal(LearningAccessResult.Allowed, outcome.Access);
        var coursework = outcome.Value!.Coursework;
        Assert.Equal(new[] { urgent.Id, later.Id, undated.Id }, coursework.PendingWriting.Select(item => item.AssignmentId));
        Assert.Collection(coursework.RecentFeedback,
            item =>
            {
                Assert.Equal(DashboardCourseFeedbackKind.DiscussionReply, item.Kind);
                Assert.Equal(newestReply.Id, item.TargetId);
            },
            item =>
            {
                Assert.Equal(DashboardCourseFeedbackKind.WritingReview, item.Kind);
                Assert.Equal(reviewed.Id, item.TargetId);
            });
        Assert.Equal(LearningAccessResult.Forbidden, (await learning.TodayAsync(teacher.Id)).Access);

        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var page = await client.GetAsync("/Learning/Dashboard");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        Assert.Contains("Việc học", html);
        Assert.Contains($"/Homework/Details/{urgent.Id}", html);
        Assert.Contains($"/Homework/Details/{reviewed.Id}", html);
        Assert.Contains($"/CourseDiscussion/Details/{newestReply.Id}", html);
        Assert.DoesNotContain(classmateReply.Subject, html);
        Assert.DoesNotContain(archiveTask.Title, html);
        Assert.DoesNotContain("Private feedback content", html);
        Assert.DoesNotContain("Private message content", html);

        Course Course(string title, CourseStatus status) =>
            new() { Title = title, Status = status, OwnerTeacherUserId = teacher.Id };

        static Enrollment Enroll(Course course, Guid userId) => new() { Course = course, StudentUserId = userId };

        WritingAssignment Assignment(Course course, string title, DateTimeOffset? deadline = null) =>
            new() { Course = course, Title = title, Prompt = "Write about work.", DueAtUtc = deadline, CreatedAtUtc = now.AddDays(-20) };

        WritingSubmission Submission(Guid userId, DateTimeOffset? review = null) =>
            new() { StudentUserId = userId, Response = "Private answer.", ReviewedAtUtc = review,
                Feedback = review.HasValue ? "Private feedback content" : null, CreatedAtUtc = now.AddDays(-18) };

        CourseDiscussionThread Thread(Enrollment enrollment, string subject) =>
            new() { Enrollment = enrollment, Subject = subject, CreatedAtUtc = now.AddDays(-3),
                Messages = [Message(enrollment.StudentUserId, false, 1, now.AddDays(-3)),
                    Message(teacher.Id, true, 2, now.AddDays(-1))] };

        static CourseDiscussionMessage Message(Guid userId, bool isTeacher, int sequence, DateTimeOffset at) =>
            new() { AuthorUserId = userId, WrittenByTeacher = isTeacher, SequenceNumber = sequence,
                Body = "Private message content", CreatedAtUtc = at };
    }
}
