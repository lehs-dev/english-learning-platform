using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Application.Practice;

/// <summary>
/// Đọc, soạn câu hỏi và xuất bản Practice dành cho Teacher.
/// Không thực hiện việc làm bài hoặc chấm bài của Student.
/// </summary>
public interface ITeacherPracticeQuestionService
{
    /// <summary>
    /// Đọc Practice và các câu hỏi đã chọn để hiển thị màn hình soạn.
    /// Chỉ trả dữ liệu khi tài khoản là Teacher hoạt động và sở hữu Practice.
    /// Không tạo hoặc cập nhật dữ liệu.
    /// </summary>
    Task<LearningOutcome<TeacherPracticeQuestionEditor>> GetEditorAsync(
        Guid actorUserId,
        Guid practiceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Thay danh sách câu hỏi bằng danh sách Teacher gửi lên.
    /// Service phải kiểm tra quyền sở hữu và tính hợp lệ trước khi lưu.
    /// Với bài đã xuất bản, tạo phiên bản mới và giữ nguyên dữ liệu các phiên bản cũ.
    /// </summary>
    Task<WriteOutcome> UpdateQuestionsAsync(
        Guid actorUserId,
        Guid practiceId,
        UpdatePracticeQuestionsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra toàn bộ điều kiện và Publish Practice.
    /// Draft được chọn parent; Practice từng Publish không được đổi parent.
    /// </summary>
    Task<WriteOutcome> PublishAsync(
        Guid actorUserId,
        Guid practiceId,
        Guid? lessonId,
        Guid? moduleId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ngừng cho Student làm Practice.
    /// Không xóa kết quả, không mở khóa câu hỏi hoặc parent.
    /// </summary>
    Task<WriteOutcome> UnpublishAsync(
        Guid actorUserId,
        Guid practiceId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);
}
