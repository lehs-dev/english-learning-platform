using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

public sealed class CourseProgressService(AppDbContext db)
{
    public async Task<Dictionary<Guid, CourseProgress>> GetAsync(Guid userId, Guid[] courseIds, CancellationToken ct)
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

    public async Task<List<ModuleOutline>> CompletedOutlineAsync(Guid userId, Guid courseId, List<ModuleOutline> modules, CancellationToken ct)
    {
        var completed = (await db.LessonProgressEntries.AsNoTracking().Where(p => p.IsCompleted &&
            p.Enrollment.StudentUserId == userId && p.Enrollment.CourseId == courseId)
            .Select(p => p.LessonId).ToListAsync(ct)).ToHashSet();
        return modules.Select(m => m with { Lessons = m.Lessons.Select(l => l with { IsCompleted = completed.Contains(l.Id) }).ToArray() }).ToList();
    }

    // Run within the caller's course transaction; historical completion is never cleared.
    public Task<int> ReconcileCompletionAsync(Guid courseId, CancellationToken ct) =>
        CourseCompletion.ReconcileAsync(db, courseId, ct);
}
