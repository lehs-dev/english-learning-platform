namespace EnglishLearningPlatform.Application.Assessment;

/// <summary>
/// Chỉ nhận Course cần gắn và phiên bản form.
/// Owner, trạng thái và quyền thay đổi luôn được đọc lại ở server.
/// </summary>
public sealed class AssessmentScopeRequest
{
    // Null nghĩa là Assessment độc lập; Guid.Empty không phải Course hợp lệ.
    public Guid? CourseId { get; set; }

    // Chặn một tab cũ ghi đè dữ liệu đã được lưu từ tab khác.
    public DateTimeOffset? ExpectedUpdatedAtUtc { get; set; }
}
