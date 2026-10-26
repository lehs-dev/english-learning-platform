using EnglishLearningPlatform.Application.Administration;

namespace EnglishLearningPlatform.Web.Models.Admin;

public sealed record AdminDashboardViewModel(AdminDashboardSummary Summary, AdminRevenueViewModel Revenue, DateOnly Today)
{
    public decimal MonthRevenue => Revenue.Report.Summary.Sum(row => row.Revenue);
    public int MonthPaidOrders => Revenue.Report.Summary.Sum(row => row.PaidOrders);
}
