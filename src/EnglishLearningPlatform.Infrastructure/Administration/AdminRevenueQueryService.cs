using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Administration;

public sealed class AdminRevenueQueryService(AppDbContext db) : IAdminRevenueQueryService
{
    public async Task<AdminRevenueReport?> GetAsync(Guid actorId, AdminDateRange? dates = null,
        string? currency = null, CancellationToken ct = default)
    {
        if (!await db.HasActiveRoleAsync(actorId, AppRoles.Admin, ct)) return null;
        dates ??= new(null, null);
        if (!dates.IsValid) throw new ArgumentException("Invalid date range.", nameof(dates));
        currency = string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant();
        if (currency != "VND")
            throw new ArgumentException("Checkout and revenue reporting support VND only.", nameof(currency));

        var start = dates.StartUtc;
        var end = dates.EndExclusiveUtc;
        var orders = db.Orders.AsNoTracking().Where(o => o.Currency == "VND");

        // Deduplicate before filtering dates: retries must never put one order into two periods.
        // Missing/invalid historical receipts are not invented as revenue. Confirmation rules remain untouched.
        var receipts = db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Succeeded && p.Amount > 0 &&
                p.Amount == p.Order.Amount && p.PaidAtUtc.HasValue)
            .GroupBy(p => p.OrderId)
            .Select(g => new { OrderId = g.Key, PaidAt = g.Min(p => p.PaidAtUtc!.Value) });
        var revenue = from receipt in receipts
                      join order in orders on receipt.OrderId equals order.Id
                      select new { Currency = order.Currency.ToUpper(), order.Amount, receipt.PaidAt };
        if (start.HasValue) revenue = revenue.Where(r => r.PaidAt >= start.Value);
        if (end.HasValue) revenue = revenue.Where(r => r.PaidAt < end.Value);

        // Each materialized row is already a SQL aggregate, never a raw Order/Payment.
        var revenueTotals = await revenue.GroupBy(r => r.Currency)
            .Select(g => new { Currency = g.Key, Revenue = g.Sum(r => r.Amount), PaidOrders = g.Count() })
            .ToListAsync(ct);
        var daily = await revenue.GroupBy(r => new
            { r.Currency, Year = EF.Functions.AtTimeZone(r.PaidAt, "SE Asia Standard Time").Year, Month = EF.Functions.AtTimeZone(r.PaidAt, "SE Asia Standard Time").Month, Day = EF.Functions.AtTimeZone(r.PaidAt, "SE Asia Standard Time").Day })
            .Select(g => new { g.Key.Currency, g.Key.Year, g.Key.Month, g.Key.Day,
                Revenue = g.Sum(r => r.Amount), PaidOrders = g.Count() })
            .OrderBy(r => r.Year).ThenBy(r => r.Month).ThenBy(r => r.Day).ThenBy(r => r.Currency)
            .ToListAsync(ct);
        var monthly = await revenue.GroupBy(r => new
            { r.Currency, Year = EF.Functions.AtTimeZone(r.PaidAt, "SE Asia Standard Time").Year, Month = EF.Functions.AtTimeZone(r.PaidAt, "SE Asia Standard Time").Month })
            .Select(g => new { g.Key.Currency, g.Key.Year, g.Key.Month,
                Revenue = g.Sum(r => r.Amount), PaidOrders = g.Count() })
            .OrderBy(r => r.Year).ThenBy(r => r.Month).ThenBy(r => r.Currency)
            .ToListAsync(ct);

        var statusFlags = db.Payments.AsNoTracking().GroupBy(p => p.OrderId)
            .Select(g => new
            {
                OrderId = g.Key,
                Succeeded = g.Max(p => p.Status == PaymentStatus.Succeeded ? 1 : 0),
                Failed = g.Max(p => p.Status == PaymentStatus.Failed ? 1 : 0),
                Cancelled = g.Max(p => p.Status == PaymentStatus.Cancelled ? 1 : 0)
            });
        var cohort = orders;
        if (start.HasValue) cohort = cohort.Where(o => o.CreatedAtUtc >= start.Value);
        if (end.HasValue) cohort = cohort.Where(o => o.CreatedAtUtc < end.Value);
        var orderStatuses = from order in cohort
                            join flags in statusFlags on order.Id equals flags.OrderId into flagRows
                            from flags in flagRows.DefaultIfEmpty()
                            join receipt in receipts on order.Id equals receipt.OrderId into receiptRows
                            from receipt in receiptRows.DefaultIfEmpty()
                            select new
                            {
                                Currency = order.Currency.ToUpper(),
                                Succeeded = (int?)flags.Succeeded ?? 0,
                                Failed = (int?)flags.Failed ?? 0,
                                Cancelled = (int?)flags.Cancelled ?? 0,
                                ReceiptOrderId = (Guid?)receipt.OrderId
                            };
        // Checkout precedence: any success wins, then failure, cancellation and finally pending.
        // Separate joins keep SQL SUM expressions free from correlated aggregate subqueries.
        var statusTotals = await orderStatuses.GroupBy(o => o.Currency)
            .Select(g => new
            {
                Currency = g.Key,
                Orders = g.Count(),
                Succeeded = g.Sum(o => o.Succeeded),
                Failed = g.Sum(o => o.Succeeded == 0 && o.Failed == 1 ? 1 : 0),
                Cancelled = g.Sum(o => o.Succeeded == 0 && o.Failed == 0 && o.Cancelled == 1 ? 1 : 0),
                Pending = g.Sum(o => o.Succeeded == 0 && o.Failed == 0 && o.Cancelled == 0 ? 1 : 0),
                Unrecognized = g.Sum(o => o.Succeeded == 1 && !o.ReceiptOrderId.HasValue ? 1 : 0)
            }).ToListAsync(ct);

        var currencies = revenueTotals.Select(r => r.Currency).Union(statusTotals.Select(r => r.Currency))
            .Order(StringComparer.Ordinal).ToList();
        var summary = currencies.Select(code =>
        {
            var paid = revenueTotals.FirstOrDefault(r => r.Currency == code);
            var status = statusTotals.FirstOrDefault(r => r.Currency == code);
            return new AdminRevenueSummary(code, paid?.Revenue ?? 0, paid?.PaidOrders ?? 0,
                status?.Orders ?? 0, status?.Succeeded ?? 0, status?.Pending ?? 0,
                status?.Failed ?? 0, status?.Cancelled ?? 0, status?.Unrecognized ?? 0);
        }).ToList();

        return new(dates, currency, summary,
            daily.Select(r => new AdminRevenuePeriod(new(r.Year, r.Month, r.Day), r.Currency, r.Revenue, r.PaidOrders)).ToList(),
            monthly.Select(r => new AdminRevenuePeriod(new(r.Year, r.Month, 1), r.Currency, r.Revenue, r.PaidOrders)).ToList());
    }
}
