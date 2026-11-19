using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Flashcards;

public sealed record FlashcardScheduleResult(DateTimeOffset NextDueAtUtc, int Repetitions);

public static class FlashcardSchedule
{
    private static readonly int[] GoodIntervalsInDays = [1, 3, 7, 14, 30];

    // Again restarts the sequence. Hard keeps the current stage and schedules tomorrow.
    public static FlashcardScheduleResult Calculate(DateTimeOffset reviewedAtUtc, int repetitions, FlashcardRating rating)
    {
        if (repetitions < 0) throw new ArgumentOutOfRangeException(nameof(repetitions));
        return rating switch
        {
            FlashcardRating.Again => new(reviewedAtUtc.AddMinutes(10), 0),
            FlashcardRating.Hard => new(reviewedAtUtc.AddDays(1), repetitions),
            FlashcardRating.Good => new(reviewedAtUtc.AddDays(GoodIntervalsInDays[Math.Min(repetitions, GoodIntervalsInDays.Length - 1)]),
                repetitions == int.MaxValue ? repetitions : repetitions + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(rating))
        };
    }
}
