using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Practice;

/// <summary>
/// Các điều kiện Teacher gửi lên khi xem danh sách Practice.
/// Class này chỉ chứa dữ liệu, chưa thực hiện truy vấn database.
/// </summary>
public sealed class TeacherPracticeSearchRequest
{
    // Tìm theo tiêu đề Practice.
    // Null hoặc chuỗi trắng được hiểu là không tìm theo tiêu đề.
    public string? Search { get; set; }

    // Null nghĩa là không lọc trạng thái.
    public PracticeStatus? Status { get; set; }

    // Null nghĩa là không lọc Course.
    // Khi lọc một Course cụ thể, Draft chưa có parent sẽ không thuộc Course đó.
    public Guid? CourseId { get; set; }

    // Trang đầu tiên bắt đầu từ số 1.
    public int PageNumber { get; set; } = 1;

    // Mặc định hiển thị tối đa 10 Practice trên mỗi trang.
    // Service sẽ kiểm tra giới hạn này trước khi truy vấn.
    public int PageSize { get; set; } = 10;
}