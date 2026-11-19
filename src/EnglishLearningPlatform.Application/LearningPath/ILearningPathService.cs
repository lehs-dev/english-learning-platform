using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.LearningPath;

public enum LearningPathReadiness { Ready, NeedsAssessment, NoWeakSkills, InsufficientContent }
public sealed record LearningPathItemView(Guid Id, int DayIndex, LearningPlanTargetType TargetType,
    Guid TargetId, string Title, int EstimatedMinutes, EnglishSkill Skill, LearningPlanItemStatus Status,
    int Position, DateTimeOffset? CompletedAtUtc);
public sealed record LearningPathPlanView(Guid Id, DateOnly StartDate, LearningPlanStatus Status,
    Guid? SourceAssessmentAttemptId, IReadOnlyList<LearningPathItemView> Items)
{
    public int DurationDays { get; init; } = LearningPlan.DefaultDurationDays;
    public DateOnly EndDate => StartDate.AddDays(DurationDays - 1);
}
public sealed record LearningPathPage(LearningPathReadiness Readiness, LearningPathPlanView? Plan,
    DateOnly Today, IReadOnlyList<EnglishSkill> WeakSkills)
{
    public bool CanGenerate => Plan is null || Plan.Status != LearningPlanStatus.Active || Today > Plan.EndDate;
    public IReadOnlyList<int> AvailableDurations { get; init; } = [];
    public int? UnavailableRequestedDuration { get; init; }
}

public interface ILearningPathService
{
    Task<LearningOutcome<LearningPathPage>> GetActivePlanAsync(Guid studentId, CancellationToken ct = default);
    Task<LearningOutcome<LearningPathPage>> GeneratePlanAsync(Guid studentId, CancellationToken ct = default);
    Task<LearningOutcome<LearningPathPage>> GeneratePlanAsync(Guid studentId, int durationDays, CancellationToken ct = default);
    Task<LearningAccessResult> SkipItemAsync(Guid studentId, Guid planItemId, CancellationToken ct = default);
    // Called only after a lesson, practice or flashcard review has been saved. Completion is rechecked in the database.
    Task TrackCompletionAsync(Guid studentId, LearningPlanTargetType type, Guid targetId, CancellationToken ct = default);
}
