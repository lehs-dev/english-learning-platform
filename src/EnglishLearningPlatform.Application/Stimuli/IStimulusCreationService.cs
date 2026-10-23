using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Application.Stimuli;

/// <summary>
/// Tạo ngữ liệu thuộc tài khoản Teacher đang hoạt động.
/// </summary>
public interface IStimulusCreationService
{
    // actorUserId phải lấy từ thông tin đăng nhập, không lấy từ form hoặc URL.
    // WriteOutcome đã có trong dự án, chứa quyền truy cập, ID mới và lỗi theo trường.
    Task<WriteOutcome> CreateAsync(
        Guid actorUserId,
        CreateStimulusRequest request,
        CancellationToken cancellationToken = default);
}
