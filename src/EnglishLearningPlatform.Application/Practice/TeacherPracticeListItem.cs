using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Practice;

/// <summary>
/// Dữ liệu của một Practice trên trang danh sách dành cho Teacher.
/// Đây không phải entity EF và không tạo thêm bảng trong database.
/// </summary>
public sealed class TeacherPracticeListItem
{
    public Guid Id { get; set; }

    // Khởi tạo chuỗi rỗng để thuộc tính luôn có giá trị không null.
    public string Title { get; set; } = string.Empty;

    public PracticeStatus Status { get; set; }

    // Số câu hỏi đã được đưa vào Practice.
    // Draft vừa tạo có thể chưa có câu hỏi nào.
    public int QuestionCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    // Course được xác định thông qua Lesson hoặc Module chứa Practice.
    // Draft chưa gắn parent thì các thông tin này có thể null.
    public Guid? CourseId { get; set; }

    public string? CourseTitle { get; set; }

    // Chỉ có giá trị khi Practice gắn trực tiếp với Module.
    // Nếu Practice gắn với Lesson, không điền ModuleId của Lesson vào đây.
    public Guid? ModuleId { get; set; }

    public string? ModuleTitle { get; set; }

    // Chỉ có giá trị khi Practice gắn trực tiếp với Lesson.
    public Guid? LessonId { get; set; }

    public string? LessonTitle { get; set; }
}