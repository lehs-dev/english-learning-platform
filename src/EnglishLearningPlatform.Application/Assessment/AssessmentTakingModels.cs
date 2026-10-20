using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Assessment;

public sealed record AssessmentTakingSheet(
    Guid AttemptId,
    Guid AssessmentId,
    string Title,
    string? Description,
    int DurationMinutes,
    DateTimeOffset DeadlineUtc,
    IReadOnlyList<AssessmentQuestionTakingModel> Questions);

public sealed record AssessmentQuestionTakingModel(
    Guid AssessmentQuestionId,
    Guid QuestionId,
    string Content,
    decimal Points,
    string? StimulusContent,
    Guid? SelectedOptionId,
    IReadOnlyList<AssessmentOptionTakingModel> Options)
{
    public string? AudioUrl { get; init; }
}

public sealed record AssessmentOptionTakingModel(
    Guid Id,
    string Content,
    int OrderIndex);

public sealed record AssessmentResultView(
    Guid AttemptId,
    Guid AssessmentId,
    string AssessmentTitle,
    Guid? CourseId,
    decimal OverallScore,
    decimal? PassingScore,
    bool Passed,
    DateTimeOffset FinalizedAtUtc,
    AttemptFinalizationReason FinalizationReason,
    IReadOnlyList<SkillScoreBreakdown> SkillBreakdowns);

public sealed record SkillScoreBreakdown(
    EnglishSkill Skill,
    decimal Score,
    int QuestionCount,
    // Server so sánh điểm gốc với ngưỡng 60%, không dùng Score đã làm tròn.
    bool IsWeak);

public sealed record AssessmentOverviewModel(
    Guid AssessmentId,
    Guid? CourseId,
    string Title,
    string? Description,
    int DurationMinutes,
    int MaxAttempts,
    decimal? PassingScore,
    int UsedAttempts,
    decimal? HighestScore,
    Guid? ActiveAttemptId,
    DateTimeOffset? ActiveDeadlineUtc)
{
    public AssessmentStatus Status { get; init; }
    public int QuestionCount { get; init; }
    public IReadOnlyList<AssessmentAttemptHistoryModel> AttemptHistory { get; init; } = [];
}

public sealed record AssessmentAttemptHistoryModel(
    Guid AttemptId,
    AttemptStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset DeadlineUtc,
    DateTimeOffset? FinalizedAtUtc,
    decimal? OverallScore,
    bool? Passed)
{
    // Chỉ mở kết quả khi đã chấm và đủ metadata; không tạo link hỏng cho dữ liệu cũ.
    public bool CanViewResult { get; init; }
}

// Mỗi nhóm chỉ chứa những lượt của Student đang đăng nhập, không mang theo đề/đáp án.
public sealed record AssessmentHistoryView(
    Guid AssessmentId,
    string AssessmentTitle,
    AssessmentStatus Status,
    IReadOnlyList<AssessmentAttemptHistoryModel> Attempts);
