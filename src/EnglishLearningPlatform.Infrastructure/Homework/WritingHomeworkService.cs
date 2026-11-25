using System.Data;
using EnglishLearningPlatform.Application.Homework;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Gamification;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Homework;

public sealed class WritingHomeworkService(AppDbContext db, IActiveUserRoleReader users, TimeProvider time) : IWritingHomeworkService
{
    public async Task<LearningOutcome<WritingHomeworkCourse>> GetCourseAsync(Guid actorId, Guid courseId, int page, CancellationToken ct)
    {
        var access = await GetAccessAsync(actorId, courseId, ct);
        if (access.Value is not { } course) return new(access.Access);

        page = NormalizePage(page);
        var now = time.GetUtcNow();
        var rows = await db.WritingAssignments.AsNoTracking().Where(row => row.CourseId == courseId)
            .OrderByDescending(row => row.CreatedAtUtc).ThenBy(row => row.Id)
            .Skip((page - 1) * WritingHomeworkLimits.PageSize).Take(WritingHomeworkLimits.PageSize + 1)
            .Select(row => new WritingHomeworkItem(row.Id, row.Title, row.DueAtUtc,
                course.IsTeacher ? row.Submissions.Count(s => s.ReviewedAtUtc == null) : null,
                !course.IsTeacher && row.Submissions.Any(s => s.StudentUserId == actorId),
                !course.IsTeacher && row.Submissions.Any(s => s.StudentUserId == actorId && s.ReviewedAtUtc != null),
                course.IsOpen && (!row.DueAtUtc.HasValue || row.DueAtUtc > now)))
            .ToListAsync(ct);
        return new(LearningAccessResult.Allowed, new(courseId, course.Title, course.IsTeacher && course.IsOpen,
            rows.Take(WritingHomeworkLimits.PageSize).ToArray(), page, rows.Count > WritingHomeworkLimits.PageSize));
    }

    public async Task<LearningOutcome<WritingHomeworkCourseInfo>> GetAuthoringCourseAsync(Guid teacherId, Guid courseId, CancellationToken ct)
    {
        var access = await GetAccessAsync(teacherId, courseId, ct);
        if (access.Value is not { } course) return new(access.Access);
        return course.IsTeacher
            ? new(LearningAccessResult.Allowed, new(courseId, course.Title, course.IsOpen))
            : new(LearningAccessResult.Forbidden);
    }

    public async Task<LearningOutcome<WritingHomeworkDetail>> GetAssignmentAsync(Guid actorId, Guid assignmentId, int page, CancellationToken ct)
    {
        var assignment = await db.WritingAssignments.AsNoTracking()
            .Where(row => row.Id == assignmentId)
            .Select(row => new { row.CourseId, row.Title, row.Prompt, row.DueAtUtc }).SingleOrDefaultAsync(ct);
        if (assignment is null) return new(LearningAccessResult.NotFound);

        var access = await GetAccessAsync(actorId, assignment.CourseId, ct);
        if (access.Value is not { } course) return new(access.Access);

        page = course.IsTeacher ? NormalizePage(page) : 1;
        var rows = await db.WritingSubmissions.AsNoTracking()
            .Where(row => row.AssignmentId == assignmentId && (course.IsTeacher || row.StudentUserId == actorId))
            .OrderBy(row => row.ReviewedAtUtc).ThenBy(row => row.CreatedAtUtc).ThenBy(row => row.Id)
            .Skip((page - 1) * WritingHomeworkLimits.PageSize).Take(WritingHomeworkLimits.PageSize + 1)
            .Select(row => new WritingSubmissionItem(row.Id,
                course.IsTeacher ? db.Users.Where(user => user.Id == row.StudentUserId).Select(user => user.FullName).FirstOrDefault() : null,
                row.Response, row.CreatedAtUtc, row.Feedback, row.Score, row.ReviewedAtUtc)).ToListAsync(ct);
        var canSubmit = !course.IsTeacher && course.IsOpen && rows.Count == 0 &&
            (!assignment.DueAtUtc.HasValue || assignment.DueAtUtc > time.GetUtcNow());
        var draft = !course.IsTeacher && rows.Count == 0
            ? await db.WritingSubmissionDrafts.AsNoTracking()
                .Where(row => row.AssignmentId == assignmentId && row.StudentUserId == actorId)
                .Select(row => new WritingDraft(row.Response, row.SavedAtUtc, row.Version)).SingleOrDefaultAsync(ct)
            : null;
        return new(LearningAccessResult.Allowed, new WritingHomeworkDetail(assignmentId, assignment.CourseId, course.Title, assignment.Title,
            assignment.Prompt, assignment.DueAtUtc, course.IsTeacher, canSubmit,
            rows.Take(WritingHomeworkLimits.PageSize).ToArray(), page, rows.Count > WritingHomeworkLimits.PageSize)
        {
            Draft = draft,
            CanReview = course.IsTeacher && course.IsOpen
        });
    }

    public async Task<WritingHomeworkWriteResult> AssignAsync(Guid teacherId, Guid courseId, string? title, string? prompt,
        DateTimeOffset? dueAtUtc, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var access = await GetAuthoringCourseAsync(teacherId, courseId, ct);
        if (access.Value is not { } course) return Denied(access.Access);
        if (!course.CanAssign) return new(WritingHomeworkWriteStatus.Closed);

        var now = time.GetUtcNow();
        title = title?.Trim();
        prompt = prompt?.Trim();
        if (title is not { Length: > 0 and <= WritingHomeworkLimits.TitleLength } ||
            prompt is not { Length: > 0 and <= WritingHomeworkLimits.PromptLength } ||
            dueAtUtc <= now)
            return new(WritingHomeworkWriteStatus.Invalid);

        var assignment = new WritingAssignment
        {
            CourseId = courseId, Title = title, Prompt = prompt, DueAtUtc = dueAtUtc?.ToUniversalTime(), CreatedAtUtc = now
        };
        db.WritingAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(WritingHomeworkWriteStatus.Saved, assignment.Id);
    }

    public async Task<WritingHomeworkWriteResult> SaveDraftAsync(Guid studentId, Guid assignmentId, string? response,
        Guid? expectedVersion, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if (await SubmissionBlockedAsync(studentId, assignmentId, ct) is { } blocked)
            return new(blocked);
        if (string.IsNullOrWhiteSpace(response) || response.Length > WritingHomeworkLimits.ResponseLength)
            return new(WritingHomeworkWriteStatus.Invalid);

        var draft = await db.WritingDraftsForUpdate(assignmentId, studentId).SingleOrDefaultAsync(ct);
        if (await db.WritingSubmissions.AnyAsync(row => row.AssignmentId == assignmentId && row.StudentUserId == studentId, ct))
            return new(WritingHomeworkWriteStatus.AlreadySubmitted, assignmentId);
        if (draft?.Response == response)
            return new(WritingHomeworkWriteStatus.Saved, assignmentId);
        if (draft?.Version != expectedVersion)
            return new(WritingHomeworkWriteStatus.DraftChanged, assignmentId);

        if (draft is null)
        {
            draft = new WritingSubmissionDraft { AssignmentId = assignmentId, StudentUserId = studentId };
            db.WritingSubmissionDrafts.Add(draft);
        }

        draft.Response = response;
        draft.Version = Guid.NewGuid();
        draft.SavedAtUtc = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(WritingHomeworkWriteStatus.Saved, assignmentId);
    }

    public async Task<WritingHomeworkWriteResult> SubmitAsync(Guid studentId, Guid assignmentId, string? response, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if (await SubmissionBlockedAsync(studentId, assignmentId, ct) is { } blocked)
            return new(blocked);
        var now = time.GetUtcNow();
        response = response?.Trim();
        if (response is not { Length: > 0 and <= WritingHomeworkLimits.ResponseLength })
            return new(WritingHomeworkWriteStatus.Invalid);

        // Saving a draft and submitting lock the same key, including when the draft is absent.
        var draft = await db.WritingDraftsForUpdate(assignmentId, studentId).SingleOrDefaultAsync(ct);
        var submission = new WritingSubmission
        {
            AssignmentId = assignmentId, StudentUserId = studentId, Response = response, CreatedAtUtc = now
        };
        db.WritingSubmissions.Add(submission);
        if (draft is not null) db.WritingSubmissionDrafts.Remove(draft);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.Entry(submission).State = EntityState.Detached;
            if (draft is not null) db.Entry(draft).State = EntityState.Unchanged;
            return new(WritingHomeworkWriteStatus.AlreadySubmitted, assignmentId);
        }

        await StudentActivityDays.RecordAsync(db, studentId, now, ct);
        await transaction.CommitAsync(ct);
        return new(WritingHomeworkWriteStatus.Saved, assignmentId);
    }

    public async Task<WritingHomeworkWriteResult> ReviewAsync(Guid teacherId, Guid assignmentId, Guid submissionId,
        string? feedback, int? score, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var assignment = await db.WritingAssignments.AsNoTracking().Where(row => row.Id == assignmentId)
            .Select(row => new { row.CourseId }).SingleOrDefaultAsync(ct);
        if (assignment is null) return new(WritingHomeworkWriteStatus.NotFound);
        var access = await GetAuthoringCourseAsync(teacherId, assignment.CourseId, ct);
        if (access.Value is not { } course) return Denied(access.Access);
        if (!course.CanAssign) return new(WritingHomeworkWriteStatus.Closed);

        feedback = feedback?.Trim();
        if (feedback is not { Length: > 0 and <= WritingHomeworkLimits.FeedbackLength } || score is < 0 or > 100)
            return new(WritingHomeworkWriteStatus.Invalid);

        var target = db.WritingSubmissions.Where(row => row.Id == submissionId && row.AssignmentId == assignmentId &&
            row.Assignment.Course.OwnerTeacherUserId == teacherId);
        var now = time.GetUtcNow();
        var updated = await target.Where(row => row.ReviewedAtUtc == null)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Feedback, feedback)
                .SetProperty(row => row.Score, score).SetProperty(row => row.ReviewedAtUtc, now)
                .SetProperty(row => row.UpdatedAtUtc, now), ct);
        if (updated > 0)
        {
            await transaction.CommitAsync(ct);
            return new(WritingHomeworkWriteStatus.Saved, assignmentId);
        }
        return new(await target.AnyAsync(ct) ? WritingHomeworkWriteStatus.AlreadyReviewed : WritingHomeworkWriteStatus.NotFound, assignmentId);
    }

    private async Task<WritingHomeworkWriteStatus?> SubmissionBlockedAsync(Guid studentId, Guid assignmentId, CancellationToken ct)
    {
        var assignment = await db.WritingAssignments.AsNoTracking().Where(row => row.Id == assignmentId)
            .Select(row => new { row.CourseId, row.DueAtUtc }).SingleOrDefaultAsync(ct);
        if (assignment is null) return WritingHomeworkWriteStatus.NotFound;
        var access = await GetAccessAsync(studentId, assignment.CourseId, ct);
        if (access.Value is not { } course) return Denied(access.Access).Status;
        if (course.IsTeacher) return WritingHomeworkWriteStatus.Forbidden;
        return !course.IsOpen || assignment.DueAtUtc <= time.GetUtcNow()
            ? WritingHomeworkWriteStatus.Closed : null;
    }

    private async Task<LearningOutcome<CourseAccess>> GetAccessAsync(Guid actorId, Guid courseId, CancellationToken ct)
    {
        var role = await users.GetActiveRoleAsync(actorId, ct);
        if (role is not (AppRoles.Teacher or AppRoles.Student)) return new(LearningAccessResult.Forbidden);

        var course = await db.Courses.AsNoTracking().Where(row => row.Id == courseId)
            .Select(row => new { row.OwnerTeacherUserId, row.Title, row.Status }).SingleOrDefaultAsync(ct);
        if (course is null) return new(LearningAccessResult.NotFound);

        if (role == AppRoles.Teacher)
        {
            return course.OwnerTeacherUserId == actorId
                ? new(LearningAccessResult.Allowed, new(course.Title, true, course.Status != CourseStatus.Archived))
                : new(LearningAccessResult.Forbidden);
        }

        var visible = course.Status is CourseStatus.Published or CourseStatus.Unpublished or CourseStatus.Archived;
        var enrolled = visible && await db.Enrollments.AsNoTracking().Valid()
            .AnyAsync(row => row.CourseId == courseId && row.StudentUserId == actorId, ct);
        return enrolled
            ? new(LearningAccessResult.Allowed, new(course.Title, false, course.Status != CourseStatus.Archived))
            : new(LearningAccessResult.Forbidden);
    }

    private static WritingHomeworkWriteResult Denied(LearningAccessResult access) =>
        new(access == LearningAccessResult.NotFound ? WritingHomeworkWriteStatus.NotFound : WritingHomeworkWriteStatus.Forbidden);

    private static int NormalizePage(int page) => Math.Clamp(page, 1, int.MaxValue / WritingHomeworkLimits.PageSize);

    private sealed record CourseAccess(string Title, bool IsTeacher, bool IsOpen);
}
