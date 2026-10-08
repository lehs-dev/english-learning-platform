using System.Data;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

public sealed partial class CourseService
{
    private Task<Course?> LockCourseAsync(Guid id, CancellationToken ct) => db.Courses
        .FromSqlInterpolated($"SELECT * FROM Courses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}")
        .SingleOrDefaultAsync(ct);

    private async Task<Dictionary<Guid, CourseProgress>> ProgressAsync(Guid userId, Guid[] courseIds, CancellationToken ct)
    {
        var enrollments = await db.Enrollments.AsNoTracking().Valid()
            .Where(e => e.StudentUserId == userId && courseIds.Contains(e.CourseId))
            .Select(e => new { e.Id, e.CourseId, e.CompletedAtUtc, e.LastAccessedLessonId }).ToListAsync(ct);
        var lessons = await db.Lessons.AsNoTracking().Effective().Where(l => courseIds.Contains(l.Module.CourseId))
            .OrderBy(l => l.Module.OrderIndex).ThenBy(l => l.ModuleId).ThenBy(l => l.OrderIndex).ThenBy(l => l.Id)
            .Select(l => new { l.Id, l.Module.CourseId }).ToListAsync(ct);
        var completed = (await db.LessonProgressEntries.AsNoTracking().Where(p => p.IsCompleted &&
            p.Enrollment.StudentUserId == userId && courseIds.Contains(p.Enrollment.CourseId) &&
            p.Lesson.Module.CourseId == p.Enrollment.CourseId)
            .Select(p => p.LessonId).ToListAsync(ct)).ToHashSet();
        var groups = lessons.ToLookup(l => l.CourseId);
        return enrollments.ToDictionary(e => e.CourseId, e =>
        {
            var available = groups[e.CourseId].Select(l => l.Id).ToArray();
            Guid? next = e.LastAccessedLessonId.HasValue && available.Contains(e.LastAccessedLessonId.Value)
                ? e.LastAccessedLessonId : available.Where(id => !completed.Contains(id)).Select(id => (Guid?)id).FirstOrDefault()
                    ?? available.Select(id => (Guid?)id).FirstOrDefault();
            return new CourseProgress(available.Count(completed.Contains), available.Length, e.CompletedAtUtc, next);
        });
    }

    private async Task<List<ModuleOutline>> CompletedOutlineAsync(Guid userId, Guid courseId, List<ModuleOutline> modules, CancellationToken ct)
    {
        var completed = (await db.LessonProgressEntries.AsNoTracking().Where(p => p.IsCompleted &&
            p.Enrollment.StudentUserId == userId && p.Enrollment.CourseId == courseId)
            .Select(p => p.LessonId).ToListAsync(ct)).ToHashSet();
        return modules.Select(m => m with { Lessons = m.Lessons.Select(l => l with { IsCompleted = completed.Contains(l.Id) }).ToArray() }).ToList();
    }

    public async Task<LearningOutcome<ModulePage>> OpenModuleAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var allowed = await access.CheckAccessAsync(userId, LearningResourceType.Module, id, LearningOperation.ViewContent, ct);
        if (allowed != LearningAccessResult.Allowed) return new(allowed);
        var module = await db.Modules.AsNoTracking().Where(m => m.Id == id)
            .Select(m => new { m.CourseId, CourseTitle = m.Course.Title, m.Course.Status }).SingleAsync(ct);
        var course = await OpenCourseAsync(userId, module.CourseId, false, ct);
        if (course.Access != LearningAccessResult.Allowed) return new(course.Access);
        var outline = course.Value!.Modules.Single(m => m.Id == id);
        return new(allowed, new(id, module.CourseId, module.CourseTitle, outline.Title, outline.Description,
            module.Status, course.Value.IsOwner, outline.Lessons, course.Value.Progress));
    }

    // Set the requested state instead of inverting it: a repeated POST remains idempotent.
    public async Task<WriteOutcome> SetLessonCompletedAsync(Guid userId, Guid id, bool completed, CancellationToken ct = default)
    {
        var courseId = await db.Lessons.Where(l => l.Id == id).Select(l => (Guid?)l.Module.CourseId).SingleOrDefaultAsync(ct);
        if (!courseId.HasValue) return WriteOutcome.Denied(LearningAccessResult.NotFound);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockCourseAsync(courseId.Value, ct);
        var allowed = await access.CheckAccessAsync(userId, LearningResourceType.Lesson, id, LearningOperation.RecordProgress, ct);
        if (allowed != LearningAccessResult.Allowed) return WriteOutcome.Denied(allowed);
        var enrollment = await db.Enrollments.Valid().SingleAsync(e => e.StudentUserId == userId && e.CourseId == courseId, ct);
        var progress = await db.LessonProgressEntries.SingleOrDefaultAsync(p => p.EnrollmentId == enrollment.Id && p.LessonId == id, ct);
        if (progress is null)
        {
            progress = new LessonProgress { EnrollmentId = enrollment.Id, LessonId = id };
            db.LessonProgressEntries.Add(progress);
        }
        progress.IsCompleted = completed;
        progress.UpdatedAtUtc = DateTimeOffset.UtcNow;
        enrollment.LastAccessedLessonId = id;
        await db.SaveChangesAsync(ct);
        await ReconcileCompletionAsync(courseId.Value, ct);
        await tx.CommitAsync(ct);
        return WriteOutcome.Ok(id);
    }

    // Run inside the same course transaction after mutations to the effective lesson set.
    // Completion is historical and is never cleared when lessons are added/hidden later.
    private Task<int> ReconcileCompletionAsync(Guid courseId, CancellationToken ct) =>
        CourseCompletion.ReconcileAsync(db, courseId, ct);
}
