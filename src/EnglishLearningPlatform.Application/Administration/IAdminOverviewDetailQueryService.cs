using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Administration;

/// <summary>Read-only, paginated drilldowns matching the historical Dashboard counts.</summary>
public interface IAdminOverviewDetailQueryService
{
    Task<AdminPage<AdminCourseOverview>?> CoursesAsync(Guid actorId, string? search, CourseStatus? status,
        int page, CancellationToken ct = default, AdminDateRange? dates = null, AdminCourseSort sort = AdminCourseSort.Newest);

    Task<AdminPage<AdminEnrollmentOverview>?> EnrollmentsAsync(Guid actorId, string? search, bool? completed,
        int page, CancellationToken ct = default, Guid? courseId = null, AdminDateRange? dates = null);
}

public sealed record AdminCourseOverview(Guid Id, string Title, string TeacherName, string TeacherEmail,
    CourseStatus Status, DateTimeOffset CreatedAtUtc, int EnrollmentCount, int CompletedEnrollmentCount,
    int TeacherEnrollmentCount = 0);

public enum AdminCourseSort { Newest, Popular, TeacherPopular }

public sealed record AdminEnrollmentOverview(Guid Id, string StudentName, string StudentEmail,
    Guid CourseId, string CourseTitle, DateTimeOffset EnrolledAtUtc, DateTimeOffset? CompletedAtUtc);
