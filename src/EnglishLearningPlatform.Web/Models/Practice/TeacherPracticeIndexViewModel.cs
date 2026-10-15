using EnglishLearningPlatform.Application.Practice;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EnglishLearningPlatform.Web.Models.Practice;

/// <summary>
/// Dữ liệu dành cho trang danh sách Practice của Teacher.
/// Không truy vấn hoặc ghi dữ liệu vào database.
/// </summary>
public sealed class TeacherPracticeIndexViewModel
{
    // Giữ các điều kiện Teacher đã nhập.
    // View dùng để hiển thị lại từ khóa và bộ lọc sau khi tìm kiếm.
    public TeacherPracticeSearchRequest Query { get; }

    // Chứa danh sách Practice và thông tin phân trang.
    public TeacherPracticeSearchResult Result { get; }

    // Các Course dùng cho dropdown lọc.
    // Controller phải lấy từ server và chỉ đưa Course của Teacher hiện tại.
    public IReadOnlyList<SelectListItem> CourseOptions { get; set; }
        = Array.Empty<SelectListItem>();

    /// <summary>
    /// Khi tạo model, bắt buộc cung cấp bộ lọc và kết quả.
    /// Tránh để View nhận dữ liệu null rồi lỗi khi hiển thị.
    /// </summary>
    public TeacherPracticeIndexViewModel(
        TeacherPracticeSearchRequest query,
        TeacherPracticeSearchResult result)
    {
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        Query = query;
        Result = result;
    }
}