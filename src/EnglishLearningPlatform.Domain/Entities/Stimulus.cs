using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class Stimulus : BaseEntity
{
    public Guid OwnerTeacherUserId { get; set; }
    public StimulusType StimulusType { get; set; }
    public string? Title { get; set; }
    public string? ContentText { get; set; }
    public string? ResourceUrl { get; set; }
    // Thời điểm hệ thống ghi nhận nội dung bị khóa.
    // Null nghĩa là chưa có dấu khóa được lưu.
    // private set: code bên ngoài không được tự gán hoặc xóa dấu khóa.
    public DateTimeOffset? ContentLockedAtUtc { get; private set; }

    /// <summary>
    /// Ghi nhận khóa nội dung một lần.
    /// Gọi lại không làm thay đổi thời điểm khóa đã lưu.
    /// Thời gian phải do servser cung cấp, không lấy từ dữ liệu người dùng gửi lên.
    /// </summary>
    public void LockContent(DateTimeOffset lockedAtUtc)
    {
        // Đã khóa thì giữ nguyên, không ghi đè thời điểm khóa.
        if (ContentLockedAtUtc.HasValue)
        {
            return;
        }

        // Chuẩn hóa thời gian về UTC trước khi lưu.
        ContentLockedAtUtc = lockedAtUtc.ToUniversalTime();
    }

    public ICollection<Question> Questions { get; set; } = new List<Question>();
}
