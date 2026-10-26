using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Administration;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Administration;

public sealed class AdminAuditForwardingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManagementAudit_ForwardsArgumentsCancellationAndQueryResult(bool allowed)
    {
        var actorId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        AdminPage<AdminAudit>? expected = allowed
            ? new([new("admin", "teacher", "UpdateUser", null, "Active", DateTimeOffset.UtcNow)], "term", 2, 3, 45)
            : null;
        var queries = new QueryStub(Task.FromResult(expected));
        var service = new AdminManagementService(null!, null!, queries);

        var task = service.AuditAsync(actorId, " term ", 2, cancellation.Token);

        Assert.Same(queries.Result, task);
        Assert.Same(expected, await task);
        Assert.Equal((actorId, " term ", 2, cancellation.Token), queries.Arguments);
    }

    private sealed class QueryStub(Task<AdminPage<AdminAudit>?> result) : IAdminQueryService
    {
        public Task<AdminPage<AdminAudit>?> Result { get; } = result;
        public (Guid Actor, string? Search, int Page, CancellationToken Cancellation) Arguments { get; private set; }

        public Task<AdminPage<AdminAudit>?> AuditAsync(Guid actorId, string? search, int page, CancellationToken ct = default, AdminDateRange? dates = null)
        {
            Arguments = (actorId, search, page, ct);
            return Result;
        }

        public Task<AdminPage<AdminTransaction>?> TransactionsAsync(Guid actorId, string? search, PaymentStatus? status,
            int page, CancellationToken ct = default, AdminDateRange? dates = null) => throw new NotSupportedException();

        public Task<AdminPage<AdminUser>?> UsersAsync(Guid actorId, string? search, AccountStatus? status,
            int page, CancellationToken ct = default, string? role = null, AdminDateRange? dates = null) => throw new NotSupportedException();
    }
}
