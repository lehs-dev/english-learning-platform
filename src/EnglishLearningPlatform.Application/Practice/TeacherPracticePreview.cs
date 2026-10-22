using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Practice;

// DTO chỉ phục vụ xem trước, không có kết quả hoặc đáp án đúng.
public sealed class TeacherPracticePreview
{
    public Guid PracticeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PracticeStatus Status { get; set; }
    public string ParentTitle { get; set; } = string.Empty;
    public IReadOnlyList<PracticeQuestionSheet> Questions { get; set; } = new List<PracticeQuestionSheet>();
    public IReadOnlyList<string> Warnings { get; set; } = new List<string>();
}

public interface ITeacherPracticePreviewService
{
    // Chỉ đọc đề đã lưu của Teacher. Không Start/Submit/Publish hoặc tạo kết quả.
    Task<LearningOutcome<TeacherPracticePreview>> GetAsync(
        Guid actorUserId, Guid practiceId, CancellationToken cancellationToken = default);
}
