namespace EnglishLearningPlatform.Application.Assessment;

/// <summary>
/// Toàn bộ danh sách câu hỏi Teacher muốn lưu vào Assessment Draft.
/// </summary>
public sealed class UpdateAssessmentQuestionsRequest
{
    // Dùng chung version với trang cấu hình ở Task 1.
    // Hai trang không được ghi đè thay đổi mới của nhau.
    public DateTimeOffset? ExpectedUpdatedAtUtc { get; set; }

    // null: request thiếu danh sách, phải từ chối.
    // Danh sách rỗng: Teacher chủ động bỏ hết câu khỏi Draft.
    public List<AssessmentQuestionInput>? Items { get; set; }
}

/// <summary>
/// Một câu hỏi và points của lần sử dụng trong Assessment này.
/// </summary>
public sealed class AssessmentQuestionInput
{
    public Guid QuestionId { get; set; }

    // Nullable để phát hiện client không gửi points.
    // Giao diện sẽ điền mặc định 1 cho câu mới thêm.
    public decimal? Points { get; set; }
}