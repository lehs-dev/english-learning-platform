using EnglishLearningPlatform.Application.Gamification;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

namespace EnglishLearningPlatform.UnitTests;

public sealed class GamificationPolicyTests
{
    [Fact]
    public void RewardWindows_UseVietnamMidnight_AndLifetimeForLesson()
    {
        var before = DateTimeOffset.Parse("2026-11-19T16:59:59Z");
        var after = before.AddSeconds(1);
        Assert.Equal("2026-11-19", GamificationPolicy.WindowKey(GamificationEventType.PracticeSubmitted, before));
        Assert.Equal("2026-11-20", GamificationPolicy.WindowKey(GamificationEventType.PracticeSubmitted, after));
        Assert.Equal("lifetime", GamificationPolicy.WindowKey(GamificationEventType.LessonCompleted, after));
        Assert.Equal(5, GamificationPolicy.XpFor(GamificationEventType.PracticeSubmitted));
        Assert.Equal(10, GamificationPolicy.XpFor(GamificationEventType.PracticeHighScorer));
        Assert.Equal(0, GamificationPolicy.XpFor((GamificationEventType)999));
    }

    [Fact]
    public void Streaks_DeduplicateDates_AndAllowYesterdayUntilTodayEnds()
    {
        var today = new DateOnly(2026, 11, 19);
        var days = Enumerable.Range(1, 7).Select(i => today.AddDays(-i)).Append(today.AddDays(-1));
        Assert.Equal((7, 7), GamificationPolicy.Streaks(days, today));
        Assert.Equal((0, 7), GamificationPolicy.Streaks(days, today.AddDays(1)));
        Assert.Equal((8, 8), GamificationPolicy.Streaks(days.Append(today), today));
        Assert.Equal((0, 0), GamificationPolicy.Streaks([], today));
    }

    [Fact]
    public void Streaks_PreserveLongestRun_AcrossGap_AndIgnoreFuture()
    {
        var today = new DateOnly(2026, 11, 19);
        var days = Enumerable.Range(5, 9).Select(i => today.AddDays(-i)).Append(today).Append(today.AddDays(1));
        Assert.Equal((1, 9), GamificationPolicy.Streaks(days, today));
    }
}
