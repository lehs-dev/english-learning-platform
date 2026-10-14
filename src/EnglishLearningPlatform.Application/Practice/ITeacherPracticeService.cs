using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Application.Practice;

/// <summary>
/// Các thao tác quản lý Practice dành cho Teacher.
/// Tách riêng khỏi IPracticeService đang phục vụ Student.
/// </summary>
public interface ITeacherPracticeService
{
    /// <summary>
    /// Tạo Practice ở trạng thái Draft.
    /// Service phải kiểm tra quyền Teacher và ownership của parent.
    /// </summary>
    /// <param name="actorUserId">
    /// ID tài khoản đăng nhập, do server lấy từ thông tin xác thực.
    /// Không lấy ID này từ form của người dùng.
    /// </param>
    /// <param name="request">
    /// Tên, mô tả và parent mà Teacher lựa chọn.
    /// </param>
    /// <param name="cancellationToken">
    /// Cho phép hủy thao tác khi request bị hủy.
    /// </param>
    Task<WriteOutcome> CreateDraftAsync(
        Guid actorUserId,
        CreatePracticeRequest request,
        CancellationToken cancellationToken = default);
}