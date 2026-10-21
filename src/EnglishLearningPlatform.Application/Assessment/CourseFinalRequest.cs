namespace EnglishLearningPlatform.Application.Assessment;

/// <summary>Chỉ thay vai trò Final của Course, không sửa định nghĩa Assessment.</summary>
public sealed class CourseFinalRequest
{
    // Null: gỡ Final. Course không bắt buộc có Final.
    public Guid? FinalAssessmentId { get; set; }

    // Hai dấu kiểm tra tab cũ: version Course và Final đã thấy khi mở trang.
    public Guid? ExpectedFinalAssessmentId { get; set; }
    public DateTimeOffset? ExpectedUpdatedAtUtc { get; set; }
}
