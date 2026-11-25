using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Homework;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

public sealed partial class StudentLearningService
{
    private const int CourseworkPreviewSize = 4;

    public async Task<LearningOutcome<StudentDashboard>> TodayAsync(Guid studentId, CancellationToken ct = default)
    {
        var result = await DashboardAsync(studentId, ct);
        if (result.Value is not { } dashboard) return result;

        var coursework = await ReadCourseworkAsync(studentId, ct);
        return new(result.Access, dashboard with { Coursework = coursework });
    }

    private async Task<DashboardCoursework> ReadCourseworkAsync(Guid studentId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var recentEnough = now.AddDays(-14);
        var enrollments = db.Enrollments.AsNoTracking().Valid().Where(enrollment =>
            enrollment.StudentUserId == studentId &&
            (enrollment.Course.Status == CourseStatus.Published || enrollment.Course.Status == CourseStatus.Unpublished));
        var assignments = db.WritingAssignments.AsNoTracking().ReleasedAt(now)
            .Where(assignment => enrollments.Any(enrollment => enrollment.CourseId == assignment.CourseId));

        var pending = await assignments.Where(assignment =>
                (assignment.DueAtUtc == null || assignment.DueAtUtc > now) &&
                !assignment.Submissions.Any(submission => submission.StudentUserId == studentId))
            .OrderBy(assignment => assignment.DueAtUtc == null).ThenBy(assignment => assignment.DueAtUtc)
            .ThenByDescending(assignment => assignment.CreatedAtUtc).ThenBy(assignment => assignment.Id)
            .Take(CourseworkPreviewSize)
            .Select(assignment => new DashboardWritingTask(
                assignment.Id, assignment.Title, assignment.Course.Title, assignment.DueAtUtc))
            .ToListAsync(ct);

        var reviewed = await assignments.SelectMany(assignment => assignment.Submissions.Where(submission =>
                submission.StudentUserId == studentId && submission.ReviewedAtUtc >= recentEnough),
            (assignment, submission) => new { assignment.Id, assignment.Title, CourseTitle = assignment.Course.Title, submission.ReviewedAtUtc })
            .OrderByDescending(row => row.ReviewedAtUtc).ThenBy(row => row.Id)
            .Take(CourseworkPreviewSize)
            .Select(row => new DashboardCourseFeedback(DashboardCourseFeedbackKind.WritingReview, row.Id,
                row.Title, row.CourseTitle, row.ReviewedAtUtc!.Value))
            .ToListAsync(ct);

        var replies = await db.CourseDiscussionThreads.AsNoTracking()
            .Where(thread => enrollments.Any(enrollment => enrollment.Id == thread.EnrollmentId))
            .SelectMany(thread => thread.Messages.OrderByDescending(message => message.SequenceNumber).Take(1),
                (thread, message) => new { thread.Id, thread.Subject, CourseTitle = thread.Enrollment.Course.Title, Message = message })
            .Where(row => row.Message.WrittenByTeacher && row.Message.CreatedAtUtc >= recentEnough)
            .OrderByDescending(row => row.Message.CreatedAtUtc).ThenBy(row => row.Id)
            .Take(CourseworkPreviewSize)
            .Select(row => new DashboardCourseFeedback(DashboardCourseFeedbackKind.DiscussionReply, row.Id,
                row.Subject, row.CourseTitle, row.Message.CreatedAtUtc))
            .ToListAsync(ct);

        var feedback = reviewed.Concat(replies)
            .OrderByDescending(row => row.OccurredAtUtc).ThenBy(row => row.Kind).ThenBy(row => row.TargetId)
            .Take(CourseworkPreviewSize).ToArray();
        return new(pending, feedback);
    }
}
