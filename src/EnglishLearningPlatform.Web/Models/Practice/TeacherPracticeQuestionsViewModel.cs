using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.QuestionBank;

namespace EnglishLearningPlatform.Web.Models.Practice;

/// <summary>
/// Dữ liệu dành cho màn hình chọn câu hỏi của Practice.
/// Không phải entity EF và không tạo bảng trong database.
/// </summary>
public sealed class TeacherPracticeQuestionsViewModel
{
    // Practice đang mở và danh sách câu hỏi đã lưu trong Practice.
    // Controller phải lấy dữ liệu này từ service,
    // không tin Title, Status hoặc quyền sửa do trình duyệt gửi lên.
    public TeacherPracticeQuestionEditor Editor { get; }

    // Các điều kiện tìm kiếm trong Question Bank.
    // View dùng để giữ lại từ khóa, kỹ năng, trình độ...
    public QuestionSearchRequest Query { get; }

    // Kết quả tìm kiếm trong Question Bank của Teacher hiện tại.
    // Đây không phải toàn bộ danh sách câu hỏi của Practice.
    public QuestionSearchResult Result { get; }

    /// <summary>
    /// Gom ba nhóm dữ liệu để Controller chuyển cho View.
    /// Các tham số phải có dữ liệu, tránh lỗi null khi hiển thị.
    /// </summary>
    public TeacherPracticeQuestionsViewModel(
        TeacherPracticeQuestionEditor editor,
        QuestionSearchRequest query,
        QuestionSearchResult result)
    {
        if (editor == null)
        {
            throw new ArgumentNullException(nameof(editor));
        }

        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        Editor = editor;
        Query = query;
        Result = result;
    }
}
