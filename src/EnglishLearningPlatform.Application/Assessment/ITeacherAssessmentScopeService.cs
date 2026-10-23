using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Application.Assessment;

/// <summary>
/// Quản lý Independent/Course-linked, không quản lý liên kết Final.
/// </summary>
public interface ITeacherAssessmentScopeService
{
    Task<LearningOutcome<TeacherAssessmentScopeEditor>> GetEditorAsync(
        Guid actorUserId,
        Guid assessmentId,
        CancellationToken cancellationToken = default);

    Task<WriteOutcome> SaveAsync(
        Guid actorUserId,
        Guid assessmentId,
        AssessmentScopeRequest request,
        CancellationToken cancellationToken = default);
}
