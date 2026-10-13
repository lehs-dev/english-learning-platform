using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

public sealed class TeacherCourseReportService(AppDbContext db, ILearningAccessService access) : ITeacherCourseReportService
{
    public async Task<LearningOutcome<TeacherCourseReport>> GetAsync(Guid teacherId, Guid courseId,
        CourseStudentsQuery query, CancellationToken ct = default)
    {
        var allowed = await access.CheckAccessAsync(teacherId, LearningResourceType.Course, courseId, LearningOperation.ManageContent, ct);
        if (allowed != LearningAccessResult.Allowed) return new(allowed);
        var title = await db.Courses.AsNoTracking().Where(c => c.Id == courseId && c.OwnerTeacherUserId == teacherId)
            .Select(c => c.Title).SingleOrDefaultAsync(ct);
        if (title is null) return new(LearningAccessResult.Forbidden);

        // Every aggregate and page is scoped to the owner in SQL, including nested commerce queries.
        var enrollments = db.Enrollments.AsNoTracking().Valid()
            .Where(e => e.CourseId == courseId && e.Course.OwnerTeacherUserId == teacherId);
        var totalEnrolled = await enrollments.CountAsync(ct);
        var paidEnrolled = await enrollments.CountAsync(e => e.PaymentId.HasValue, ct);
        var completed = await enrollments.CountAsync(e => e.CompletedAtUtc.HasValue, ct);
        var orders = db.Orders.AsNoTracking().Where(o => o.CourseId == courseId && o.Course.OwnerTeacherUserId == teacherId);
        var receipts = db.Payments.AsNoTracking().Where(p => p.Order.CourseId == courseId &&
            p.Order.Course.OwnerTeacherUserId == teacherId && p.Status == PaymentStatus.Succeeded &&
            p.Order.Currency == "VND" && p.Amount == p.Order.Amount);
        var commerce = new CourseCommerceSummary(totalEnrolled, totalEnrolled - paidEnrolled, paidEnrolled, completed,
            await orders.CountAsync(ct), await orders.CountAsync(o => receipts.Any(p => p.OrderId == o.Id), ct),
            await receipts.SumAsync(p => (decimal?)p.Amount, ct) ?? 0m);

        query.Search = query.Search?.Trim();
        if (query.Search?.Length > 120) query.Search = query.Search[..120];
        var students = enrollments.Join(db.Users.AsNoTracking(), e => e.StudentUserId, u => u.Id, (e, u) => new { Enrollment = e, u.FullName });
        if (!string.IsNullOrEmpty(query.Search)) students = students.Where(s => s.FullName.Contains(query.Search));
        if (query.Completed.HasValue) students = students.Where(s => s.Enrollment.CompletedAtUtc.HasValue == query.Completed.Value);
        var count = await students.CountAsync(ct);
        query.Page = Math.Clamp(query.Page, 1, Math.Max(1, (count + TeacherCourseReport.PageSize - 1) / TeacherCourseReport.PageSize));
        var available = db.Lessons.AsNoTracking().Effective().Where(l => l.Module.CourseId == courseId && l.Module.Course.OwnerTeacherUserId == teacherId);
        var totalLessons = await available.CountAsync(ct);
        var rows = await students.OrderBy(s => s.FullName).ThenBy(s => s.Enrollment.StudentUserId)
            .Skip((query.Page - 1) * TeacherCourseReport.PageSize).Take(TeacherCourseReport.PageSize)
            .Select(s => new
            {
                s.Enrollment.StudentUserId, s.FullName, s.Enrollment.EnrolledAtUtc, s.Enrollment.CompletedAtUtc,
                CompletedLessons = s.Enrollment.LessonProgressEntries.Count(p => p.IsCompleted && available.Any(l => l.Id == p.LessonId))
            }).ToListAsync(ct);
        return new(allowed, new(courseId, title, query, rows.Select(s => new CourseStudent(s.StudentUserId, s.FullName,
            s.EnrolledAtUtc, new(s.CompletedLessons, totalLessons, s.CompletedAtUtc, null))).ToList(), count, commerce));
    }
}
