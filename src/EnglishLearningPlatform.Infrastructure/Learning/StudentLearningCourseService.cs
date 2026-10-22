using System.Data;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

public sealed class StudentLearningCourseService(
    AppDbContext db, ILearningAccessService access, IActiveUserValidator activeUsers,
    CourseQueries queries, CourseProgressService courseProgress) : IStudentLearningCourseService
{
    private Task<LearningAccessResult> Manage(Guid user, Guid course, CancellationToken ct) =>
        access.CheckAccessAsync(user, LearningResourceType.Course, course, LearningOperation.ManageContent, ct);

    public async Task<LearningOutcome<IReadOnlyList<CourseSummary>>> MyCoursesAsync(Guid userId, bool teacher, CancellationToken ct = default)
    {
        if (!await activeUsers.IsActiveRoleAsync(userId, teacher ? AppRoles.Teacher : AppRoles.Student, ct)) return new(LearningAccessResult.Forbidden);
        var enrollments = db.Enrollments.AsNoTracking().Valid().Where(e => e.StudentUserId == userId);
        var query = db.Courses.AsNoTracking().Where(c => teacher ? c.OwnerTeacherUserId == userId :
            (c.Status == CourseStatus.Published || c.Status == CourseStatus.Unpublished || c.Status == CourseStatus.Archived) &&
            enrollments.Any(e => e.CourseId == c.Id));
        var courses = await queries.Summaries(query.OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.Id), !teacher).ToListAsync(ct);
        if (!teacher)
        {
            var progress = await courseProgress.GetAsync(userId, courses.Select(c => c.Id).ToArray(), ct);
            courses = courses.Select(c => c with { Progress = progress.GetValueOrDefault(c.Id) }).ToList();
        }
        return new(LearningAccessResult.Allowed, courses);
    }

    public async Task<LearningOutcome<CoursePage>> OpenCourseAsync(Guid userId, Guid id, bool manage, CancellationToken ct = default)
    {
        var allowed = await access.CheckAccessAsync(userId, LearningResourceType.Course, id,
            manage ? LearningOperation.ManageContent : LearningOperation.ViewContent, ct);
        if (allowed != LearningAccessResult.Allowed) return new(allowed);
        var owner = await Manage(userId, id, ct) == LearningAccessResult.Allowed;
        var summary = await queries.Summaries(db.Courses.AsNoTracking().Where(c => c.Id == id), !owner).SingleAsync(ct);
        var errors = owner ? CourseRules.PublishErrors((await queries.Graph().AsNoTracking().SingleAsync(c => c.Id == id, ct))) : [];
        var modules = await queries.OutlineAsync(id, owner, ct);
        var progress = owner ? null : (await courseProgress.GetAsync(userId, [id], ct)).GetValueOrDefault(id);
        if (!owner) modules = await courseProgress.CompletedOutlineAsync(userId, id, modules, ct);
        return new(allowed, new(summary, modules, true, owner,
            owner ? await db.Enrollments.CountAsync(e => e.CourseId == id, ct) : 0, errors)
        {
            Progress = progress,
            FinalAssessmentId = await db.Courses.AsNoTracking().Where(c => c.Id == id).Select(c => c.FinalAssessmentId).SingleAsync(ct)
        });
    }

    public async Task<LearningOutcome<LessonPage>> OpenLessonAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var allowed = await access.CheckAccessAsync(userId, LearningResourceType.Lesson, id, LearningOperation.ViewContent, ct);
        if (allowed != LearningAccessResult.Allowed) return new(allowed);
        var lesson = await db.Lessons.AsNoTracking().Where(l => l.Id == id).Select(l => new LessonPage(l.Id,
            l.Module.CourseId, l.Module.Course.Title, l.Title, l.Resources.OrderBy(r => r.OrderIndex).ThenBy(r => r.Id)
                .Select(r => new ResourceContent(r.Id, r.Title, r.ResourceType, r.ContentText, r.ResourceUrl)).ToList())).SingleAsync(ct);
        var module = await db.Lessons.Where(l => l.Id == id).Select(l => new { l.ModuleId, l.Module.Title, l.Module.Course.Status }).SingleAsync(ct);
        var owner = await Manage(userId, lesson.CourseId, ct) == LearningAccessResult.Allowed;
        var navigation = (await queries.OutlineAsync(lesson.CourseId, owner, ct)).SelectMany(m => m.Lessons).ToList();
        var position = navigation.FindIndex(l => l.Id == id);
        var progress = owner ? null : (await courseProgress.GetAsync(userId, [lesson.CourseId], ct)).GetValueOrDefault(lesson.CourseId);
        var completed = !owner && await db.LessonProgressEntries.AnyAsync(p => p.LessonId == id && p.IsCompleted &&
            p.Enrollment.StudentUserId == userId && p.Enrollment.CourseId == lesson.CourseId, ct);
        return new(allowed, lesson with { ModuleId = module.ModuleId, ModuleTitle = module.Title, CourseStatus = module.Status,
            IsCompleted = completed, CanRecordProgress = !owner && module.Status is CourseStatus.Published or CourseStatus.Unpublished,
            Progress = progress, Previous = position > 0 ? navigation[position - 1] : null,
            Next = position >= 0 && position + 1 < navigation.Count ? navigation[position + 1] : null });
    }

    public async Task<LearningOutcome<ResourceContent>> OpenResourceAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var allowed = await access.CheckAccessAsync(userId, LearningResourceType.Resource, id, LearningOperation.ViewContent, ct);
        if (allowed != LearningAccessResult.Allowed) return new(allowed);
        return new(allowed, await db.LessonResources.AsNoTracking().Where(r => r.Id == id)
            .Select(r => new ResourceContent(r.Id, r.Title, r.ResourceType, r.ContentText, r.ResourceUrl)).SingleAsync(ct));
    }

    public async Task<WriteOutcome> EnrollFreeAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var c = await db.CoursesForUpdate(id).SingleOrDefaultAsync(ct);
        if (!await activeUsers.IsActiveStudentAsync(userId, ct)) return WriteOutcome.Denied(LearningAccessResult.Forbidden);
        if (c is null || c.Status != CourseStatus.Published) return WriteOutcome.Denied(LearningAccessResult.NotFound);
        if (c.IsPaid || c.Price != 0 || !await db.Users.AnyAsync(u => u.Id == c.OwnerTeacherUserId && u.AccountStatus != AccountStatus.Disabled, ct))
            return WriteOutcome.Denied(LearningAccessResult.Forbidden);
        // Reserve the enrollment key range before checking, so concurrent repeated POSTs
        // wait for the first insert instead of both taking shared locks then deadlocking.
        if (!await db.EnrollmentsForUpdate(userId, id).AnyAsync(ct))
        {
            db.Enrollments.Add(new Enrollment { StudentUserId = userId, CourseId = id });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct); return WriteOutcome.Ok(id);
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
        await db.CoursesForUpdate(courseId.Value).SingleOrDefaultAsync(ct);
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
        await courseProgress.ReconcileCompletionAsync(courseId.Value, ct);
        await tx.CommitAsync(ct);
        return WriteOutcome.Ok(id);
    }
}
