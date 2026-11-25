using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class LearningPlan : BaseEntity
{
    public const int DefaultDurationDays = 7;
    public const int MaximumDurationDays = 30;

    public static IReadOnlyList<int> SupportedDurations { get; } =
        Array.AsReadOnly(new[] { DefaultDurationDays, 14, MaximumDurationDays });

    public static bool IsSupportedDuration(int days) => SupportedDurations.Contains(days);

    public Guid StudentUserId { get; set; }
    public Guid? SourceAssessmentAttemptId { get; set; }
    public Attempt? SourceAssessmentAttempt { get; set; }
    public DateOnly StartDate { get; set; }
    public int DurationDays { get; set; } = DefaultDurationDays;
    public string TimezoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public LearningPlanStatus Status { get; set; } = LearningPlanStatus.Active;
    public ICollection<LearningPlanItem> Items { get; set; } = new List<LearningPlanItem>();
}
