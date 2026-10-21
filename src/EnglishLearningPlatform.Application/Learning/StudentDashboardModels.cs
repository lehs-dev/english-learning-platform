using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Learning;

public sealed record StudentDashboard(
    IReadOnlyList<CourseSummary> Courses,
    IReadOnlyList<RepresentativeAssessmentResult> Results,
    IReadOnlyList<SkillRecommendation> Recommendations)
{
    public int CompletedCourses => Courses.Count(c => c.Progress?.CompletedAtUtc.HasValue == true);
    public IReadOnlyList<DashboardPracticeResult> PracticeResults { get; init; } = [];
}
public sealed record DashboardPracticeResult(Guid PracticeId, string Title, decimal Score, DateTimeOffset SubmittedAtUtc);

public sealed record RepresentativeAssessmentResult(Guid AssessmentId, string Title, Guid AttemptId,
    decimal OverallScore, DateTimeOffset FinalizedAtUtc, IReadOnlyList<DashboardSkillResult> Skills);
public sealed record DashboardSkillResult(EnglishSkill Skill, decimal Score, int QuestionCount, bool IsWeak);
public enum RecommendedContentKind { Course, Practice, Assessment }
public sealed record RecommendedContent(RecommendedContentKind Kind, Guid Id, string Title, Guid? CourseId);
public sealed record SkillRecommendation(Guid AssessmentId, string AssessmentTitle, Guid AttemptId,
    EnglishSkill Skill, decimal Score, IReadOnlyList<RecommendedContent> Contents);

public sealed record PracticeLink(Guid Id, Guid CourseId, Guid? ModuleId, Guid? LessonId,
    string Title, int QuestionCount, IReadOnlyList<EnglishSkill> Skills);
public sealed record AssessmentLink(Guid Id, Guid? CourseId, string Title, AssessmentType Type,
    int QuestionCount, int DurationMinutes, IReadOnlyList<EnglishSkill> Skills, Guid? ActiveAttemptId)
{
    public AssessmentStatus Status { get; init; }
}
public sealed record CourseActivities(IReadOnlyList<PracticeLink> Practices, IReadOnlyList<AssessmentLink> Assessments);

public interface IStudentLearningService
{
    Task<LearningOutcome<StudentDashboard>> DashboardAsync(Guid studentId, CancellationToken ct = default);
    // Dashboard và Profile dùng chung kết quả đại diện, không tự chọn highest ở từng trang.
    Task<LearningOutcome<IReadOnlyList<RepresentativeAssessmentResult>>> GetRepresentativeResultsAsync(
        Guid studentId, CancellationToken ct = default);
    Task<LearningOutcome<CourseActivities>> ActivitiesAsync(Guid studentId, Guid courseId, CancellationToken ct = default);
    Task<LearningOutcome<IReadOnlyList<AssessmentLink>>> AssessmentsAsync(Guid studentId, CancellationToken ct = default);
}

public static class SkillAnalysisRules
{
    public const decimal WeakSkillThreshold = 60m;
    public const int MinimumQuestionCount = 3;

    // Compare the earned points before display rounding, as required by BR12/BR13.
    public static bool IsWeak(decimal earnedPoints, decimal maximumPoints, int questionCount) =>
        questionCount >= MinimumQuestionCount && maximumPoints > 0m &&
        earnedPoints * 100m < maximumPoints * WeakSkillThreshold;
}
