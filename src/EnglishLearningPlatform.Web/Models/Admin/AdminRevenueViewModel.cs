using EnglishLearningPlatform.Application.Administration;

namespace EnglishLearningPlatform.Web.Models.Admin;

public sealed class AdminRevenueViewModel(AdminRevenueReport report, string groupBy, int page, string period = "custom")
{
    public const int PageSize = 20;
    public AdminRevenueReport Report { get; } = report;
    public string GroupBy { get; } = groupBy;
    public string Period { get; } = period;
    public string PeriodTitle => Period switch { "week" => "Tuần này", "month" => "Tháng này", "year" => "Năm này", "all" => "Toàn bộ lịch sử", _ => "Khoảng đã chọn" };
    public string RangeLabel => Report.Dates switch {
        { FromDate: DateOnly start, ToDate: DateOnly end } when start == end => $"Ngày {start:dd/MM/yyyy}",
        { FromDate: DateOnly start, ToDate: DateOnly end } => $"Từ {start:dd/MM/yyyy} đến hết {end:dd/MM/yyyy}",
        { FromDate: DateOnly start } => $"Từ {start:dd/MM/yyyy} trở đi",
        { ToDate: DateOnly end } => $"Đến hết {end:dd/MM/yyyy}",
        _ => "Toàn bộ lịch sử thanh toán"
    };
    public IReadOnlyList<AdminRevenuePeriod> Series => GroupBy == "month" ? Report.Monthly : Report.Daily;
    public int Pages => Math.Max(1, (Series.Count + PageSize - 1) / PageSize);
    public int Page => Math.Clamp(page, 1, Pages);
    // Fill calendar gaps only after SQL aggregation, never raw payment records.
    // Very wide custom ranges keep aggregate points to avoid unbounded allocation.
    public IReadOnlyList<AdminRevenuePeriod> ChartPoints
    {
        get
        {
            if (Series.Count == 0) return [];
            var monthly = GroupBy == "month";
            var first = Report.Dates.FromDate ?? Series.Min(p => p.Period);
            var last = Report.Dates.ToDate ?? Series.Max(p => p.Period);
            if (monthly) { first = new(first.Year, first.Month, 1); last = new(last.Year, last.Month, 1); }
            var count = monthly ? (last.Year-first.Year)*12+last.Month-first.Month+1 : last.DayNumber-first.DayNumber+1;
            if (count is < 1 or > 3660) return Series.OrderBy(p => p.Period).ToArray();
            var values = Series.ToDictionary(p => p.Period);
            var points = new List<AdminRevenuePeriod>(count);
            var cursor = first;
            for (var i=0;i<count;i++) {
                points.Add(values.GetValueOrDefault(cursor) ?? new(cursor,"VND",0,0));
                if (i<count-1) cursor = monthly ? cursor.AddMonths(1) : cursor.AddDays(1);
            }
            return points;
        }
    }
    // These are SQL-aggregated period rows, not raw Orders or Payments.
    public IEnumerable<AdminRevenuePeriod> PageItems => Series.OrderByDescending(p => p.Period)
        .ThenBy(p => p.Currency, StringComparer.Ordinal).Skip((Page - 1) * PageSize).Take(PageSize);
}
