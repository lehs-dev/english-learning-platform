namespace EnglishLearningPlatform.Application.QuestionBank;
// Dữ liệu của một lựa chọn khi Teacher xem chi tiết câu hỏi.
// Service phải kiểm tra quyền sở hữu trước khi trả dữ liệu này.
public sealed class QuestionOptionItem
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;

    // Thứ tự hiển thị, dùng đúng tên của entity QuestionOption.
    // Dự án đang đánh số từ 0: lựa chọn đầu tiên có OrderIndex = 0.
    public int OrderIndex { get; set; }
    public bool IsCorrect { get; set; }
}
