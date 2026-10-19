using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Application.Assessment;

public interface ITeacherAssessmentPreviewService
{
    // Chỉ đọc đề đã lưu.
    // Không tạo Attempt, lưu đáp án hoặc chấm điểm.
    Task<LearningOutcome<TeacherAssessmentPreview>> GetAsync(
        Guid actorUserId,
        Guid assessmentId,
        CancellationToken cancellationToken = default);
}