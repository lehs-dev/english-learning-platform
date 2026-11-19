using System.Globalization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Gamification;

public sealed record AchievementSummary(string Code, string Title, DateTimeOffset AwardedAtUtc);
public sealed record GamificationSummary(int TotalXp, int CurrentStreak, int LongestStreak,
    IReadOnlyList<AchievementSummary> Badges);

public interface IGamificationService
{
    Task<LearningOutcome<GamificationSummary>> GetSummaryAsync(Guid studentId, CancellationToken ct = default);
    Task<bool> AwardXpAsync(Guid studentId, GamificationEventType eventType, Guid sourceId, int xp, CancellationToken ct = default);
}

public static class GamificationPolicy
{
    public const string TimezoneId = "Asia/Ho_Chi_Minh";
    public static DateOnly LocalDate(DateTimeOffset timestamp) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(timestamp, TimezoneId).DateTime);

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
        var dates = activityDates.Where(d => d <= today).Distinct().Order().ToArray();
        int longest = 0, run = 0;
        DateOnly? previous = null;
        foreach (var date in dates)
        {
            run = previous.HasValue && previous.Value.AddDays(1) == date ? run + 1 : 1;
            longest = Math.Max(longest, run);
            previous = date;
        }
        int current = dates.Length > 0 && dates[^1] >= today.AddDays(-1) ? run : 0;
        return (current, longest);
    }

    public static string BadgeTitle(string code) => code switch
    {
        "first-lesson" => "Bài học đầu tiên",
        "five-practices" => "Hoàn thành 5 bài luyện tập",
        "seven-day-streak" => "Chuỗi học 7 ngày",
        _ => code
    };
}
