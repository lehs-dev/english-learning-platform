using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Assessment;

/// <summary>
/// Một Assessment trên trang danh sách riêng của Teacher.
/// </summary>
public sealed class TeacherAssessmentListItem
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public AssessmentType AssessmentType { get; set; }

    public AssessmentStatus Status { get; set; }

    public int DurationMinutes { get; set; }

    public int MaxAttempts { get; set; }

    public decimal? PassingScore { get; set; }

    public int QuestionCount { get; set; }

    // Chỉ hiển thị scope và tên Course; không trả entity Course hoặc Enrollment.
    public Guid? CourseId { get; set; }

    public string? CourseTitle { get; set; }

    // Version riêng của đề, dùng cho các POST Unpublish/Archive từ danh sách.
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    // Chỉ phục vụ hiển thị nút Sửa.
    // Khi lưu, service vẫn phải kiểm tra lại.
    public bool CanEditDraft { get; set; }

    // Cho sửa cấu hình Draft hoặc Published chưa có Attempt.
    // CanEditDraft vẫn dành cho chức năng soạn câu hỏi Draft.
    public bool CanEditConfiguration { get; set; }
}
