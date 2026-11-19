using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.TeacherAnalytics;

public sealed class TeacherAnalyticsQuery
{
    public Guid? CourseId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
}

public sealed record AnalyticsCourse(Guid Id, string Title);
public sealed record AnalyticsSkill(EnglishSkill Skill, decimal AverageScore, int ResultCount);
public sealed record AnalyticsQuestion(Guid Id, string Content, int WrongCount, int AnswerCount);
public sealed record AnalyticsStudent(string FullName, int CompletedLessons, int TotalLessons,
    decimal? PracticeScore, decimal? AssessmentScore);
public sealed record TeacherAnalyticsPage(TeacherAnalyticsQuery Query, IReadOnlyList<AnalyticsCourse> Courses,
    int StudentCount, int EnrollmentCount, int CompletedEnrollments, decimal CompletionPercent,
    decimal? AveragePracticeScore, decimal? AverageAssessmentScore, IReadOnlyList<AnalyticsSkill> Skills,
    IReadOnlyList<AnalyticsQuestion> MostMissedQuestions, IReadOnlyList<AnalyticsStudent> Students, int StudentRows)
{
    public const int PageSize = 25;
}

public interface ITeacherAnalyticsQueryService
{
    Task<LearningOutcome<TeacherAnalyticsPage>> GetAsync(Guid teacherId, TeacherAnalyticsQuery query, CancellationToken ct = default);
}
