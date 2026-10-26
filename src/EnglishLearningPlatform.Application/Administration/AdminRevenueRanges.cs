namespace EnglishLearningPlatform.Application.Administration;

public static class AdminRevenueRanges
{
    // The caller supplies today's Vietnam calendar date. Weeks start on Monday.
    public static AdminDateRange Resolve(string period, DateOnly today, DateOnly? fromDate, DateOnly? toDate)
    {
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return period switch {
            "week" => new(monday, monday.AddDays(6)),
            "month" => new(new(today.Year, today.Month, 1), new(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month))),
            "year" => new(new(today.Year, 1, 1), new(today.Year, 12, 31)),
            "all" => new(null, null),
            "custom" => new(fromDate, toDate),
            _ => throw new ArgumentException("Invalid revenue period.", nameof(period))
        };
    }
}
