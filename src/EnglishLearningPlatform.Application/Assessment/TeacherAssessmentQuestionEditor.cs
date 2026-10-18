using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Assessment;

/// <summary>
/// Dữ liệu trang soạn câu hỏi, không phải entity EF.
/// </summary>
public sealed class TeacherAssessmentQuestionEditor
{
    public Guid AssessmentId { get; set; }

    public string Title { get; set; } = string.Empty;

    public AssessmentType AssessmentType { get; set; }

    public EnglishSkill? TargetSkill { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public IReadOnlyList<SelectedAssessmentQuestion> Items { get; set; }
        = new List<SelectedAssessmentQuestion>();
}

/// <summary>
/// Một câu đã được lưu trong đề.
/// QuestionId là ID Question, không phải ID AssessmentQuestion.
/// </summary>
public sealed class SelectedAssessmentQuestion
{
    public Guid QuestionId { get; set; }

    public string Content { get; set; } = string.Empty;

    public EnglishSkill PrimarySkill { get; set; }

    public QuestionStatus Status { get; set; }

    public decimal Points { get; set; }
}