using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Application.Assessment;

/// <summary>
/// Quản lý cấu hình Assessment dành cho Teacher.
/// Không tạo Attempt, không chấm bài và không Publish.
/// </summary>
public interface ITeacherAssessmentDraftService
{
    /// <summary>
    /// Chỉ đọc danh sách Assessment thuộc Teacher hiện tại.
    /// </summary>
    Task<LearningOutcome<IReadOnlyList<TeacherAssessmentListItem>>>
        GetListAsync(
            Guid actorUserId,
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc cấu hình Draft để mở form sửa.
    /// </summary>
    Task<LearningOutcome<AssessmentDraftRequest>> GetDraftAsync(
        Guid actorUserId,
        Guid assessmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// assessmentId null: tạo Draft mới.
    /// assessmentId có giá trị: sửa Draft đang có.
    /// </summary>
    Task<WriteOutcome> SaveDraftAsync(
        Guid actorUserId,
        Guid? assessmentId,
        AssessmentDraftRequest request,
        CancellationToken cancellationToken = default);

    // Đọc cấu hình Draft hoặc Published chưa có Attempt.
    // Các hàm Draft cũ vẫn giữ phạm vi Draft-only cho caller hiện có.
    Task<LearningOutcome<TeacherAssessmentConfigurationEditor>>
        GetConfigurationEditorAsync(
            Guid actorUserId,
            Guid assessmentId,
            CancellationToken cancellationToken = default);

    // Tạo Draft mới hoặc sửa cấu hình Draft/Published chưa có Attempt.
    // Published phải qua kiểm tra toàn bộ định nghĩa và điều kiện Final.
    Task<WriteOutcome> SaveConfigurationAsync(
        Guid actorUserId,
        Guid? assessmentId,
        AssessmentDraftRequest request,
        CancellationToken cancellationToken = default);
}
