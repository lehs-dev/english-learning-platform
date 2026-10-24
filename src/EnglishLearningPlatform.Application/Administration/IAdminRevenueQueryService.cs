namespace EnglishLearningPlatform.Application.Administration;

/// <summary>Read-only VND reporting matching checkout. Revenue dates are Vietnam calendar dates.</summary>
public interface IAdminRevenueQueryService
{
    Task<AdminRevenueReport?> GetAsync(Guid actorId, AdminDateRange? dates = null,
        string? currency = null, CancellationToken ct = default);
}

public sealed record AdminRevenueReport(AdminDateRange Dates, string? CurrencyFilter,
    IReadOnlyList<AdminRevenueSummary> Summary, IReadOnlyList<AdminRevenuePeriod> Daily,
    IReadOnlyList<AdminRevenuePeriod> Monthly);

/// <summary>Revenue uses receipt dates; order statuses use order creation dates.</summary>
public sealed record AdminRevenueSummary(string Currency, decimal Revenue, int PaidOrders,
    int Orders, int SucceededOrders, int PendingOrders, int FailedOrders, int CancelledOrders,
    int UnrecognizedSucceededOrders);

public sealed record AdminRevenuePeriod(DateOnly Period, string Currency, decimal Revenue, int PaidOrders);
