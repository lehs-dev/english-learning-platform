using System.Globalization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Gamification;

public sealed record AchievementSummary(string Code, string Title, DateTimeOffset AwardedAtUtc);
public sealed record GamificationSummary(int TotalXp, int CurrentStreak, int LongestStreak,
    IReadOnlyList<AchievementSummary> Badges)
{
    public bool StudiedToday { get; init; }
    public IReadOnlyList<DateOnly> RecentActivityDates { get; init; } = [];
}

public interface IGamificationService
{
    Task<LearningOutcome<GamificationSummary>> GetSummaryAsync(Guid studentId, CancellationToken ct = default);
    Task<bool> AwardXpAsync(Guid studentId, GamificationEventType eventType, Guid sourceId, int xp, CancellationToken ct = default);
}

public static class GamificationPolicy
{
    public const string TimezoneId = "Asia/Ho_Chi_Minh";
    public const string SevenDayStreakBadge = "seven-day-streak";
    private static readonly TimeZoneInfo VietnamTimezone = TimeZoneInfo.FindSystemTimeZoneById(TimezoneId);

    public static DateOnly LocalDate(DateTimeOffset timestamp) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestamp, VietnamTimezone).DateTime);

    public static DateTimeOffset LocalDayStartUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), VietnamTimezone);

    public static int XpFor(GamificationEventType type) => type switch
    {
        GamificationEventType.LessonCompleted => 10,
        GamificationEventType.PracticeSubmitted => 5,
        GamificationEventType.PracticeHighScorer => 10,
        GamificationEventType.FlashcardsReviewed => 5,
        GamificationEventType.DailyGoalReached => 10,
        _ => 0
    };

    public static string WindowKey(GamificationEventType type, DateTimeOffset timestamp) =>
        type == GamificationEventType.LessonCompleted ? "lifetime" : LocalDate(timestamp).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static (int Current, int Longest) Streaks(IEnumerable<DateOnly> activityDates, DateOnly today)
    {
        var progress = StreakProgress(activityDates, today);
        return (progress.Current, progress.Longest);
    }

    public static (int Current, int Longest, bool StudiedToday, DateOnly? SevenDayMilestone)
        StreakProgress(IEnumerable<DateOnly> activityDates, DateOnly today)
    {
        int longest = 0, run = 0;
        DateOnly? previous = null;
        DateOnly? sevenDayMilestone = null;
        foreach (var date in activityDates.Where(d => d <= today).Distinct().Order())
        {
            run = previous.HasValue && date.DayNumber - previous.Value.DayNumber == 1 ? run + 1 : 1;
            longest = Math.Max(longest, run);
            if (run == 7)
                sevenDayMilestone ??= date;
            previous = date;
        }
        int current = previous.HasValue && today.DayNumber - previous.Value.DayNumber <= 1 ? run : 0;
        return (current, longest, previous == today, sevenDayMilestone);
    }

    public static string BadgeTitle(string code) => code switch
    {
        "first-lesson" => "Bài học đầu tiên",
        "five-practices" => "Hoàn thành 5 bài luyện tập",
        SevenDayStreakBadge => "Chuỗi học 7 ngày",
        _ => code
    };
}
