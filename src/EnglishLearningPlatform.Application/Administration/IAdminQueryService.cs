using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Administration;

/// <summary>Read-only administrative projections; never return identity secrets or assessment answers.</summary>
public interface IAdminQueryService
{
    Task<AdminPage<AdminTransaction>?> TransactionsAsync(Guid actorId, string? search, PaymentStatus? status, int page, CancellationToken ct = default);
    Task<AdminPage<AdminUser>?> UsersAsync(Guid actorId, string? search, AccountStatus? status, int page, CancellationToken ct = default, string? role = null);
}

public sealed record AdminPage<T>(IReadOnlyList<T> Items, string Search, int Page, int Pages, int Total);

public sealed record AdminTransaction(Guid OrderId, string StudentEmail, string CourseTitle,
    decimal Amount, string Currency, DateTimeOffset CreatedAtUtc, PaymentStatus? Status, DateTimeOffset? PaidAtUtc);

public sealed record AdminUser(Guid UserId, string FullName, string Email, string Role,
    AccountStatus Status, DateTimeOffset CreatedAtUtc);
