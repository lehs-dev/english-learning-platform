namespace EnglishLearningPlatform.Application.Learning;

public sealed class CourseStudentsQuery
{
    public string? Search { get; set; }
    public bool? Completed { get; set; }
    public int Page { get; set; } = 1;
}

public sealed record CourseStudent(Guid StudentId, string Name, DateTimeOffset EnrolledAtUtc, CourseProgress Progress);
public sealed record CourseCommerceSummary(int Enrollments, int FreeEnrollments, int PaidEnrollments,
    int CompletedEnrollments, int Orders, int SuccessfulOrders, decimal SuccessfulPaymentAmount);
public sealed record TeacherCourseReport(Guid CourseId, string Title, CourseStudentsQuery Query,
    IReadOnlyList<CourseStudent> Students, int TotalStudents, CourseCommerceSummary Commerce)
{
    public const int PageSize = 20;
    public int Pages => Math.Max(1, (TotalStudents + PageSize - 1) / PageSize);
}

public interface ITeacherCourseReportService
{
    Task<LearningOutcome<TeacherCourseReport>> GetAsync(Guid teacherId, Guid courseId,
        CourseStudentsQuery query, CancellationToken ct = default);
}
