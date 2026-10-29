using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Practice;

/// <summary>
/// Dữ liệu phục vụ màn hình soạn câu hỏi của Practice.
/// Đây không phải entity EF, không tạo bảng trong database.
/// </summary>
public sealed class TeacherPracticeQuestionEditor
{
    public Guid PracticeId { get; set; }

    // Giá trị mặc định tránh cảnh báo thuộc tính không được khởi tạo.
    public string Title { get; set; } = string.Empty;

    public PracticeStatus Status { get; set; }
    public int Version { get; set; } = 1;

    // Phiên bản dữ liệu để kiểm tra khi lưu hoặc Publish.
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    // Parent hiện tại của Practice.
    public Guid? LessonId { get; set; }

    public Guid? ModuleId { get; set; }

    public string ParentTitle { get; set; } = string.Empty;

    // Chỉ chứa Lesson/Module thuộc Teacher hiện tại,
    // trong những Course chưa Archived.
    public IReadOnlyList<PracticeParentOption> LessonOptions { get; set; }
        = new List<PracticeParentOption>();

    public IReadOnlyList<PracticeParentOption> ModuleOptions { get; set; }
        = new List<PracticeParentOption>();

    // Chỉ phục vụ việc hiển thị hoặc ẩn nút trên giao diện.
    // Khi lưu, service vẫn phải kiểm tra lại quyền và trạng thái.
    public bool CanEditQuestions { get; set; }

    // Danh sách được service sắp xếp theo thứ tự trong Practice.
    // Id của từng QuestionListItem là QuestionId,
    // không phải Id của bản ghi liên kết PracticeQuestion.
    public IReadOnlyList<QuestionListItem> Questions { get; set; }
        = new List<QuestionListItem>();
}

/// <summary>
/// Một lựa chọn Lesson hoặc Module trên màn hình Publish.
/// Không phải entity database.
/// </summary>
public sealed class PracticeParentOption
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;
}
