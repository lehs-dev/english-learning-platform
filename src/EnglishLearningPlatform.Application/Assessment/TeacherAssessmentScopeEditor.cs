using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Assessment;

/// <summary>
/// Metadata để Teacher xem/chọn phạm vi truy cập của đề.
/// Không chứa câu hỏi, đáp án hoặc kết quả Student.
/// </summary>
public sealed class TeacherAssessmentScopeEditor
{
    public Guid AssessmentId { get; init; }
    public string Title { get; init; } = string.Empty;
    public AssessmentStatus Status { get; init; }
    public Guid? CourseId { get; init; }
    public string? CourseTitle { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public bool HasAttempt { get; init; }
    public bool IsFinal { get; init; }
    public bool CanChangeScope { get; init; }
    public string? LockReason { get; init; }

    // Server chỉ đưa Course cùng owner, chưa Archived vào danh sách chọn.
    public IReadOnlyList<AssessmentScopeCourseOption> Courses { get; init; }
        = Array.Empty<AssessmentScopeCourseOption>();
}

public sealed record AssessmentScopeCourseOption(
    Guid Id,
    string Title,
    CourseStatus Status);
