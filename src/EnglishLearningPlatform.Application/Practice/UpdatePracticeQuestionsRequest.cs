namespace EnglishLearningPlatform.Application.Practice;

/// <summary>
/// Danh sách câu hỏi Teacher muốn lưu vào một Practice Draft.
/// Đây là dữ liệu đầu vào, không phải entity lưu trong database.
/// </summary>
public sealed class UpdatePracticeQuestionsRequest
{
    // Thời điểm cập nhật Practice khi Teacher mở trang.
    // Service so sánh với database để phát hiện trang đã cũ,
    // tránh hai tab vô tình ghi đè danh sách của nhau.
    public DateTimeOffset? ExpectedUpdatedAtUtc { get; set; }

    // Mỗi phần tử là ID của một Question trong Question Bank.
    // Không nhận nội dung hoặc đáp án từ form:
    // service phải đọc dữ liệu thật từ database để kiểm tra.
    //
    // Thứ tự trong danh sách cũng là thứ tự câu hỏi trong Practice.
    // Service sẽ tạo OrderIndex tương ứng: 0, 1, 2...
    //
    // Draft được phép chưa có câu hỏi nên danh sách rỗng là hợp lệ.
    // Điều kiện có ít nhất một câu hỏi sẽ kiểm tra khi Publish.
    public List<Guid> QuestionIds { get; set; } = new List<Guid>();
}
