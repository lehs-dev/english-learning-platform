using EnglishLearningPlatform.Application.Flashcards;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Flashcards;

public sealed class FlashcardScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    [Fact]
    public void Again_resets_repetitions_and_is_due_in_ten_minutes()
    {
        var result = FlashcardSchedule.Calculate(Now, 4, FlashcardRating.Again);
        Assert.Equal(0, result.Repetitions);
        Assert.Equal(Now.AddMinutes(10), result.NextDueAtUtc);
    }
    [Fact]
    public void Hard_retains_stage_and_schedules_tomorrow()
    {
        var result = FlashcardSchedule.Calculate(Now, 3, FlashcardRating.Hard);
        Assert.Equal(3, result.Repetitions);
        Assert.Equal(Now.AddDays(1), result.NextDueAtUtc);
    }
    [Theory]
    [InlineData(0, 1)] [InlineData(1, 3)] [InlineData(2, 7)] [InlineData(3, 14)] [InlineData(4, 30)] [InlineData(100, 30)]
    public void Good_progressively_extends_interval_with_a_thirty_day_cap(int repetitions, int days)
    {
        var result = FlashcardSchedule.Calculate(Now, repetitions, FlashcardRating.Good);
        Assert.Equal(repetitions + 1, result.Repetitions);
        Assert.Equal(Now.AddDays(days), result.NextDueAtUtc);
    }
    [Fact]
    public void Invalid_rating_and_negative_repetitions_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FlashcardSchedule.Calculate(Now, 0, (FlashcardRating)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlashcardSchedule.Calculate(Now, -1, FlashcardRating.Good));
    }
    [Fact]
    public void Repetition_counter_does_not_overflow()
    {
        var result = FlashcardSchedule.Calculate(Now, int.MaxValue, FlashcardRating.Good);
        Assert.Equal(int.MaxValue, result.Repetitions);
        Assert.Equal(Now.AddDays(30), result.NextDueAtUtc);
    }
}
