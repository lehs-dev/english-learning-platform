using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Assessment;

// DTO dành riêng cho Preview, không phải entity EF.
// Không tạo bảng mới trong database.
public sealed class TeacherAssessmentPreview
{
    public Guid AssessmentId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public AssessmentType AssessmentType { get; set; }

    public AssessmentStatus Status { get; set; }

    public EnglishSkill? TargetSkill { get; set; }

    public Guid? CourseId { get; set; }

    public string? CourseTitle { get; set; }

    public int DurationMinutes { get; set; }

    public int MaxAttempts { get; set; }

    public decimal? PassingScore { get; set; }

    // Danh sách này không gắn với Attempt nào.
    public IReadOnlyList<AssessmentQuestionTakingModel> Questions
        { get; set; } = new List<AssessmentQuestionTakingModel>();

    // Cảnh báo giúp Teacher biết Draft đang thiếu hoặc sai phần nào.
    // Danh sách rỗng không có nghĩa bài đã được Publish.
    public IReadOnlyList<string> Warnings
        { get; set; } = new List<string>();
}