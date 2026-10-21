using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Assessment;

public sealed class TeacherCourseFinalEditor
{
    public Guid CourseId { get; init; }
    public string CourseTitle { get; init; } = string.Empty;
    public CourseStatus CourseStatus { get; init; }
    public Guid? FinalAssessmentId { get; init; }
    public string? FinalAssessmentTitle { get; init; }
    public AssessmentStatus? FinalAssessmentStatus { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public bool HasFinalAttempts { get; init; }
    public bool CanChangeFinal { get; init; }
    public string? LockReason { get; init; }

    // Chỉ chứa đề Published cùng Course/owner, đúng loại và có PassingScore.
    public IReadOnlyList<FinalAssessmentOption> Assessments { get; init; }
        = Array.Empty<FinalAssessmentOption>();
}

public sealed record FinalAssessmentOption(
    Guid Id, string Title, AssessmentType AssessmentType, decimal PassingScore);
