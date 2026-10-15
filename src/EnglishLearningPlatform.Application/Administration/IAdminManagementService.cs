using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Administration;

public interface IAdminManagementService
{
    Task<AdminChangeResult> CreateTeacherAsync(Guid actorId, string name, string email, string password, CancellationToken ct = default);
    Task<AdminChangeResult> UpdateUserAsync(Guid actorId, Guid targetId, string role, AccountStatus status, CancellationToken ct = default);
    Task<AdminPage<AdminAudit>?> AuditAsync(Guid actorId, string? search, int page, CancellationToken ct = default);
}

public sealed record AdminChangeResult(bool Success, string? Error = null);
public sealed record AdminAudit(string Actor, string? Target, string Action, string? Before, string? After, DateTimeOffset OccurredAtUtc);
