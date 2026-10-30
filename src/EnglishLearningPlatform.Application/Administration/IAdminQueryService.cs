using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Administration;

/// <summary>Read-only administrative projections; never return identity secrets or assessment answers.</summary>
public interface IAdminQueryService
{
    Task<AdminPage<AdminAudit>?> AuditAsync(Guid actorId, string? search, int page, CancellationToken ct = default, AdminDateRange? dates = null);
    Task<AdminPage<AdminTransaction>?> TransactionsAsync(Guid actorId, string? search, PaymentStatus? status, int page, CancellationToken ct = default, AdminDateRange? dates = null);
    Task<AdminPage<AdminUser>?> UsersAsync(Guid actorId, string? search, AccountStatus? status, int page, CancellationToken ct = default, string? role = null, AdminDateRange? dates = null);
}

public sealed record AdminPage<T>(IReadOnlyList<T> Items, string Search, int Page, int Pages, int Total);

public sealed record AdminTransaction(Guid OrderId, string StudentEmail, string CourseTitle,
    decimal Amount, string Currency, DateTimeOffset CreatedAtUtc, PaymentStatus? Status, DateTimeOffset? PaidAtUtc)
{
    public string Provider { get; init; } = "";
    public string? ProviderTransactionId { get; init; }
}

public sealed record AdminUser(Guid UserId, string FullName, string Email, string Role,
    AccountStatus Status, DateTimeOffset CreatedAtUtc);

/// <summary>Calendar dates in Vietnam (UTC+7), with an inclusive end date.</summary>
public sealed record AdminDateRange(DateOnly? FromDate, DateOnly? ToDate)
{
    public bool IsValid => (!FromDate.HasValue || !ToDate.HasValue || FromDate <= ToDate)
        && FromDate != DateOnly.MinValue && ToDate != DateOnly.MinValue && ToDate != DateOnly.MaxValue;
    public DateTimeOffset? StartUtc => FromDate.HasValue
        ? new DateTimeOffset(FromDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime() : null;
    public DateTimeOffset? EndExclusiveUtc => ToDate.HasValue
        ? new DateTimeOffset(ToDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime() : null;
}
