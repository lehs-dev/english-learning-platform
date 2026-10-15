using System.ComponentModel.DataAnnotations;

namespace EnglishLearningPlatform.Application.Practice;

/// <summary>
/// Dữ liệu Teacher nhập khi tạo một Practice bản nháp.
/// Đây không phải entity được lưu trực tiếp vào database.
/// </summary>
public sealed class CreatePracticeRequest
{
    // Title trong database có giới hạn 200 ký tự.
    // Giá trị mặc định giúp tránh cảnh báo non-nullable.
    [Required(ErrorMessage = "Bạn phải nhập tên bài luyện tập.")]
    [StringLength(
        200,
        ErrorMessage = "Tên bài luyện tập không được vượt quá 200 ký tự.")]
    public string Title { get; set; } = string.Empty;

    // Mô tả không bắt buộc.
    // Giới hạn này khớp với mapping hiện tại.
    [StringLength(
        4000,
        ErrorMessage = "Mô tả không được vượt quá 4000 ký tự.")]
    public string? Description { get; set; }

    // Gắn Practice vào một Lesson.
    // null nghĩa là chưa chọn Lesson.
    public Guid? LessonId { get; set; }

    // Gắn Practice vào một Module.
    // Không được đồng thời có cả LessonId và ModuleId.
    public Guid? ModuleId { get; set; }
}