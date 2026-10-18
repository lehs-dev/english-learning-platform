using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Application.Assessment;

public interface ITeacherAssessmentQuestionService
{
    /// <summary>
    /// Đọc đề và câu hỏi đã chọn, không ghi dữ liệu.
    /// </summary>
    Task<LearningOutcome<TeacherAssessmentQuestionEditor>>
        GetEditorAsync(
            Guid actorUserId,
            Guid assessmentId,
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Lưu toàn bộ danh sách theo thứ tự Teacher gửi lên.
    /// Phải kiểm tra hết dữ liệu trước khi thay danh sách cũ.
    /// </summary>
    Task<WriteOutcome> SaveAsync(
        Guid actorUserId,
        Guid assessmentId,
        UpdateAssessmentQuestionsRequest request,
        CancellationToken cancellationToken = default);
}