using System.Data;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

public sealed class CourseDiscussionService(AppDbContext db, IActiveUserRoleReader users, TimeProvider time)
    : ICourseDiscussionService
{
    private const int ThreadPageSize = 20;
    private const int MessagePageSize = 30;

    public async Task<LearningOutcome<CourseDiscussionInbox>> GetInboxAsync(Guid userId, Guid courseId, int page,
        CancellationToken ct = default)
    {
        var role = await users.GetActiveRoleAsync(userId, ct);
        if (role is not (AppRoles.Student or AppRoles.Teacher)) return new(LearningAccessResult.Forbidden);

        var isTeacher = role == AppRoles.Teacher;
        var enrollments = AccessibleEnrollments(userId, isTeacher);
        var course = await db.Courses.AsNoTracking().Where(c => c.Id == courseId &&
                (isTeacher ? c.OwnerTeacherUserId == userId : enrollments.Any(e => e.CourseId == c.Id)))
            .Select(c => new CourseDiscussionHeader(c.Id, c.Title, isTeacher,
                c.Status == CourseStatus.Published || c.Status == CourseStatus.Unpublished))
            .SingleOrDefaultAsync(ct);
        if (course is null) return new(LearningAccessResult.NotFound);

        var threads = AccessibleThreads(enrollments).Where(t => t.Enrollment.CourseId == courseId);
        var pages = Pages(await threads.CountAsync(ct), ThreadPageSize);
        page = Math.Clamp(page, 1, pages);
        var rows = await threads.Select(t => new
            {
                t.Id,
                t.Subject,
                ParticipantName = db.Users.Where(u => u.Id == (isTeacher ? t.Enrollment.StudentUserId : t.Enrollment.Course.OwnerTeacherUserId))
                    .Select(u => u.FullName != "" ? u.FullName : u.UserName).FirstOrDefault() ?? "",
                LastMessageAtUtc = t.Messages.OrderByDescending(m => m.SequenceNumber).Select(m => m.CreatedAtUtc).FirstOrDefault(),
                IsAwaitingTeacher = !t.Messages.OrderByDescending(m => m.SequenceNumber).Select(m => m.WrittenByTeacher).FirstOrDefault()
            })
            .OrderByDescending(t => t.LastMessageAtUtc).ThenBy(t => t.Id)
            .Skip((page - 1) * ThreadPageSize).Take(ThreadPageSize)
            .Select(t => new CourseDiscussionThreadSummary(t.Id, t.Subject, t.ParticipantName, t.LastMessageAtUtc, t.IsAwaitingTeacher))
            .ToListAsync(ct);

        return new(LearningAccessResult.Allowed, new(course, page, pages, rows));
    }

    public async Task<LearningOutcome<CourseDiscussionConversation>> GetConversationAsync(Guid userId, Guid threadId,
        int page, CancellationToken ct = default)
    {
        var role = await users.GetActiveRoleAsync(userId, ct);
        if (role is not (AppRoles.Student or AppRoles.Teacher)) return new(LearningAccessResult.Forbidden);

        var isTeacher = role == AppRoles.Teacher;
        var thread = await AccessibleThreads(AccessibleEnrollments(userId, isTeacher)).Where(t => t.Id == threadId)
            .Select(t => new
            {
                t.Subject,
                Course = new CourseDiscussionHeader(t.Enrollment.CourseId, t.Enrollment.Course.Title, isTeacher,
                    t.Enrollment.Course.Status == CourseStatus.Published || t.Enrollment.Course.Status == CourseStatus.Unpublished),
                ParticipantName = db.Users
                    .Where(u => u.Id == (isTeacher ? t.Enrollment.StudentUserId : t.Enrollment.Course.OwnerTeacherUserId))
                    .Select(u => u.FullName != "" ? u.FullName : u.UserName).FirstOrDefault() ?? ""
            }).SingleOrDefaultAsync(ct);
        if (thread is null) return new(LearningAccessResult.NotFound);

        var messages = db.CourseDiscussionMessages.AsNoTracking().Where(m => m.ThreadId == threadId);
        var pages = Pages(await messages.CountAsync(ct), MessagePageSize);
        page = Math.Clamp(page, 1, pages);
        var rows = await messages.OrderByDescending(m => m.SequenceNumber)
            .Skip((page - 1) * MessagePageSize).Take(MessagePageSize)
            .Select(m => new CourseDiscussionMessageItem(m.Body, m.CreatedAtUtc, m.WrittenByTeacher, m.AuthorUserId == userId))
            .ToListAsync(ct);
        rows.Reverse();

        return new(LearningAccessResult.Allowed,
            new(thread.Course, threadId, thread.Subject, thread.ParticipantName, page, pages, rows));
    }

    public async Task<WriteOutcome> AskAsync(Guid studentId, Guid courseId, Guid requestId, string? subject, string? body,
        CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Share the course lock used by publishing and other course mutations. The form ID
        // makes replay safe and the initial message is committed together with its thread.
        var status = await db.CoursesForUpdate(courseId).Select(c => (CourseStatus?)c.Status).SingleOrDefaultAsync(ct);
        if (await users.GetActiveRoleAsync(studentId, ct) != AppRoles.Student)
            return WriteOutcome.Denied(LearningAccessResult.Forbidden);
        var enrollmentId = await AccessibleEnrollments(studentId, false).Where(e => e.CourseId == courseId)
            .Select(e => (Guid?)e.Id).SingleOrDefaultAsync(ct);
        if (!enrollmentId.HasValue) return WriteOutcome.Denied(LearningAccessResult.NotFound);
        if (status is not (CourseStatus.Published or CourseStatus.Unpublished))
            return WriteOutcome.Denied(LearningAccessResult.Forbidden);

        var errors = Validate(requestId, body);
        subject = subject?.Trim();
        if (subject is null or { Length: 0 } || subject.Length > CourseDiscussionLimits.SubjectLength)
            errors["subject"] = [$"Nhập chủ đề từ 1 đến {CourseDiscussionLimits.SubjectLength} ký tự."];
        if (errors.Count > 0) return new(LearningAccessResult.Allowed, Guid.Empty, errors);

        var existingEnrollmentId = await db.CourseDiscussionThreads.AsNoTracking().Where(t => t.Id == requestId)
            .Select(t => (Guid?)t.EnrollmentId).SingleOrDefaultAsync(ct);
        if (existingEnrollmentId.HasValue)
            return existingEnrollmentId == enrollmentId ? WriteOutcome.Ok(requestId) : WriteOutcome.Denied(LearningAccessResult.NotFound);

        var thread = new CourseDiscussionThread { Id = requestId, EnrollmentId = enrollmentId.Value, Subject = subject!, CreatedAtUtc = time.GetUtcNow() };
        thread.Messages.Add(NewMessage(thread.Id, Guid.NewGuid(), studentId, false, 1, body!));
        db.CourseDiscussionThreads.Add(thread);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return WriteOutcome.Ok(thread.Id);
    }

    public async Task<WriteOutcome> ReplyAsync(Guid userId, Guid threadId, Guid requestId, string? body, CancellationToken ct = default)
    {
        var role = await users.GetActiveRoleAsync(userId, ct);
        if (role is not (AppRoles.Student or AppRoles.Teacher)) return WriteOutcome.Denied(LearningAccessResult.Forbidden);

        var isTeacher = role == AppRoles.Teacher;
        var thread = AccessibleThreads(AccessibleEnrollments(userId, isTeacher)).Where(t => t.Id == threadId);
        var courseId = await thread.Select(t => (Guid?)t.Enrollment.CourseId).SingleOrDefaultAsync(ct);
        if (courseId is null) return WriteOutcome.Denied(LearningAccessResult.NotFound);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var status = await db.CoursesForUpdate(courseId.Value).Select(c => (CourseStatus?)c.Status).SingleOrDefaultAsync(ct);
        if (await users.GetActiveRoleAsync(userId, ct) != role)
            return WriteOutcome.Denied(LearningAccessResult.Forbidden);
        if (!await thread.AnyAsync(ct)) return WriteOutcome.Denied(LearningAccessResult.NotFound);
        if (status is not (CourseStatus.Published or CourseStatus.Unpublished))
            return WriteOutcome.Denied(LearningAccessResult.Forbidden);

        var errors = Validate(requestId, body);
        if (errors.Count > 0) return new(LearningAccessResult.Allowed, Guid.Empty, errors);

        var messages = db.CourseDiscussionMessages.AsNoTracking();
        var existing = await messages.Where(m => m.Id == requestId)
            .Select(m => new { m.ThreadId, m.AuthorUserId }).SingleOrDefaultAsync(ct);
        if (existing is not null)
            return existing.ThreadId == threadId && existing.AuthorUserId == userId
                ? WriteOutcome.Ok(threadId) : WriteOutcome.Denied(LearningAccessResult.NotFound);

        var last = await messages.Where(m => m.ThreadId == threadId).MaxAsync(m => (int?)m.SequenceNumber, ct) ?? 0;
        db.CourseDiscussionMessages.Add(NewMessage(threadId, requestId, userId, isTeacher, last + 1, body!));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return WriteOutcome.Ok(threadId);
    }

    private IQueryable<Enrollment> AccessibleEnrollments(Guid userId, bool isTeacher)
    {
        var enrollments = db.Enrollments.AsNoTracking().Valid();
        return isTeacher
            ? enrollments.Where(e => e.Course.OwnerTeacherUserId == userId)
            : enrollments.Where(e => e.StudentUserId == userId &&
                (e.Course.Status == CourseStatus.Published || e.Course.Status == CourseStatus.Unpublished ||
                 e.Course.Status == CourseStatus.Archived));
    }

    private IQueryable<CourseDiscussionThread> AccessibleThreads(IQueryable<Enrollment> enrollments) =>
        db.CourseDiscussionThreads.AsNoTracking().Where(t => enrollments.Any(e => e.Id == t.EnrollmentId));

    private static Dictionary<string, string[]> Validate(Guid requestId, string? body)
    {
        var errors = new Dictionary<string, string[]>();
        if (requestId == Guid.Empty) errors[""] = ["Phiên gửi không hợp lệ. Hãy thử gửi lại."];
        body = body?.Trim();
        if (body is null or { Length: 0 } || body.Length > CourseDiscussionLimits.BodyLength)
            errors["body"] = [$"Nhập nội dung từ 1 đến {CourseDiscussionLimits.BodyLength} ký tự."];
        return errors;
    }

    private CourseDiscussionMessage NewMessage(Guid threadId, Guid requestId, Guid userId, bool isTeacher,
        int sequenceNumber, string body) => new()
    {
        Id = requestId, ThreadId = threadId, AuthorUserId = userId, WrittenByTeacher = isTeacher,
        SequenceNumber = sequenceNumber, Body = body.Trim(), CreatedAtUtc = time.GetUtcNow()
    };

    private static int Pages(int count, int pageSize) => Math.Max(1, (count + pageSize - 1) / pageSize);
}
