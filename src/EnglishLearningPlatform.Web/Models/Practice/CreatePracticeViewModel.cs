using EnglishLearningPlatform.Application.Practice;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EnglishLearningPlatform.Web.Models.Practice;

/// <summary>
/// Dữ liệu để hiển thị form tạo Practice Draft.
/// Không truy cập database và không thực hiện việc lưu Practice.
/// </summary>
public sealed class CreatePracticeViewModel
{
    // Những thông tin Teacher nhập để tạo Practice.
    // Tái sử dụng request đã có, không tạo lại các trường tương tự.
    public CreatePracticeRequest Input { get; set; }
        = new CreatePracticeRequest();

    // Course đang được chọn để hiển thị danh sách Lesson/Module.
    // Đây là ngữ cảnh của form, không phải một cột mới trong Practice.
    public Guid? CourseId { get; set; }

    // Tên Course được server lấy để hiển thị.
    // Không sử dụng tên do người dùng gửi lên để kiểm tra quyền.
    public string? CourseTitle { get; set; }

    // Các Course thuộc Teacher đang đăng nhập.
    // Controller sẽ lấy danh sách qua service hiện có.
    public IReadOnlyList<SelectListItem> CourseOptions { get; set; }
        = Array.Empty<SelectListItem>();

    // Các Module trong Course đã chọn.
    // Chưa chọn Course thì danh sách này rỗng.
    public IReadOnlyList<SelectListItem> ModuleOptions { get; set; }
        = Array.Empty<SelectListItem>();

    // Các Lesson trong Course đã chọn.
    // Không đưa Lesson của Teacher khác vào danh sách.
    public IReadOnlyList<SelectListItem> LessonOptions { get; set; }
        = Array.Empty<SelectListItem>();
}