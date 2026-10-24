namespace EnglishLearningPlatform.Application.Administration;

/// <summary>System-wide, read-only counts. Returns null unless the actor is an active Admin.</summary>
public interface IAdminDashboardQueryService
{
    Task<AdminDashboardSummary?> GetAsync(Guid actorId, CancellationToken ct = default);
}

/// <summary>Historical record counts; role totals include locked and disabled accounts.</summary>
public sealed record AdminDashboardSummary(
    int Users, int Locked, int Disabled,
    int Students, int Teachers,
    int Courses, int PublishedCourses, int DraftCourses,
    int Enrollments, int CompletedEnrollments,
    int Orders, int SucceededOrders,
    int UnpublishedCourses = 0, int ArchivedCourses = 0, int Admins = 0)
{
    public int IncompleteEnrollments => Math.Max(0, Enrollments - CompletedEnrollments);
    public decimal? CompletionRate => Enrollments == 0 ? null
        : decimal.Round(100m * CompletedEnrollments / Enrollments, 1, MidpointRounding.AwayFromZero);
}
