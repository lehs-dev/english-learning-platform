using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Administration;

public sealed class AdminOverviewDetailQueryService(AppDbContext db) : IAdminOverviewDetailQueryService
{
    private const int PageSize = 20;

    public async Task<AdminPage<AdminCourseOverview>?> CoursesAsync(Guid actorId, string? search, CourseStatus? status,
        int page, CancellationToken ct = default)
    {
        if (!await db.HasActiveRoleAsync(actorId, AppRoles.Admin, ct)) return null;
        if (status.HasValue && !Enum.IsDefined(status.Value))
            throw new ArgumentException("Invalid course status.", nameof(status));

        var term = Search(search);
        // Do not filter the teacher's current role/status: match the Dashboard's historical counts.
        var query = from course in db.Courses.AsNoTracking()
                    join teacher in db.Users.AsNoTracking() on course.OwnerTeacherUserId equals teacher.Id into teachers
                    from teacher in teachers.DefaultIfEmpty()
                    select new
                    {
                        Course = course,
                        TeacherName = teacher != null ? teacher.FullName : "",
                        TeacherEmail = teacher != null ? teacher.Email ?? "" : ""
                    };
        if (term.Length > 0)
        {
            var isId = Guid.TryParse(term, out var courseId);
            query = query.Where(row => row.Course.Title.Contains(term) || row.TeacherName.Contains(term) ||
                row.TeacherEmail.Contains(term) || (isId && row.Course.Id == courseId));
        }
        if (status.HasValue) query = query.Where(row => row.Course.Status == status.Value);

        var total = await query.CountAsync(ct);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);
        // Count, filter and pagination run in SQL; correlated counts do not load Enrollment rows.
        var items = await query.OrderByDescending(row => row.Course.CreatedAtUtc).ThenByDescending(row => row.Course.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(row => new AdminCourseOverview(row.Course.Id, row.Course.Title, row.TeacherName,
                row.TeacherEmail, row.Course.Status, row.Course.CreatedAtUtc,
                row.Course.Enrollments.Count(), row.Course.Enrollments.Count(e => e.CompletedAtUtc.HasValue)))
            .ToListAsync(ct);
        return new(items, term, page, pages, total);
    }

    public async Task<AdminPage<AdminEnrollmentOverview>?> EnrollmentsAsync(Guid actorId, string? search, bool? completed,
        int page, CancellationToken ct = default, Guid? courseId = null)
    {
        if (!await db.HasActiveRoleAsync(actorId, AppRoles.Admin, ct)) return null;

        var term = Search(search);
        var query = from enrollment in db.Enrollments.AsNoTracking()
                    join student in db.Users.AsNoTracking() on enrollment.StudentUserId equals student.Id into students
                    from student in students.DefaultIfEmpty()
                    select new
                    {
                        Enrollment = enrollment,
                        StudentName = student != null ? student.FullName : "",
                        StudentEmail = student != null ? student.Email ?? "" : ""
                    };
        if (term.Length > 0)
        {
            var isId = Guid.TryParse(term, out var enrollmentId);
            query = query.Where(row => row.Enrollment.Course.Title.Contains(term) || row.StudentName.Contains(term) ||
                row.StudentEmail.Contains(term) || (isId && row.Enrollment.Id == enrollmentId));
        }
        if (completed.HasValue)
            query = query.Where(row => row.Enrollment.CompletedAtUtc.HasValue == completed.Value);
        if (courseId.HasValue) query = query.Where(row => row.Enrollment.CourseId == courseId.Value);

        var total = await query.CountAsync(ct);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);
        var items = await query.OrderByDescending(row => row.Enrollment.EnrolledAtUtc).ThenByDescending(row => row.Enrollment.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(row => new AdminEnrollmentOverview(row.Enrollment.Id, row.StudentName, row.StudentEmail,
                row.Enrollment.CourseId, row.Enrollment.Course.Title,
                row.Enrollment.EnrolledAtUtc, row.Enrollment.CompletedAtUtc))
            .ToListAsync(ct);
        return new(items, term, page, pages, total);
    }

    private static string Search(string? search)
    {
        var term = (search ?? "").Trim();
        return term.Length > 120 ? term[..120] : term;
    }
}
