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

    // Chỉ phục vụ hiển thị nút Sửa.
    // Khi lưu, service vẫn phải kiểm tra lại.
    public bool CanEditDraft { get; set; }
}