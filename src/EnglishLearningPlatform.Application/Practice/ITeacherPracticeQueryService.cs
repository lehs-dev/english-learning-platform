using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Application.Practice;

/// <summary>
/// Các thao tác đọc Practice dành riêng cho Teacher.
/// Tách khỏi service tạo Draft để mỗi service có trách nhiệm rõ ràng.
/// </summary>
public interface ITeacherPracticeQueryService
{
    /// <summary>
    /// Tìm và phân trang Practice thuộc tài khoản Teacher hiện tại.
    /// Service phải kiểm tra tài khoản Teacher còn hoạt động
    /// và lọc OwnerTeacherUserId trước khi trả dữ liệu.
    /// Không tạo hoặc thay đổi Practice, Attempt hay kết quả.
    /// </summary>
    /// <param name="actorUserId">
    /// ID tài khoản do server lấy từ thông tin đăng nhập.
    /// Không nhận ID chủ sở hữu do người dùng tự gửi lên.
    /// </param>
    /// <param name="request">
    /// Từ khóa, bộ lọc và thông tin trang muốn xem.
    /// </param>
    /// <param name="cancellationToken">
    /// Cho phép hủy truy vấn khi request bị hủy.
    /// </param>
    Task<LearningOutcome<TeacherPracticeSearchResult>> SearchAsync(
        Guid actorUserId,
        TeacherPracticeSearchRequest request,
        CancellationToken cancellationToken = default);
}