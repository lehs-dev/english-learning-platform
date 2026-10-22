using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Identity;

public sealed class ActiveUserValidator(UserManager<ApplicationUser> users, AppDbContext? db = null)
    : IActiveUserValidator, IActiveUserRoleReader
{
    public async Task<string?> GetActiveRoleAsync(Guid userId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (userId == Guid.Empty) return null;

        if (db is not null)
            return await db.ActiveRoleAssignments().Where(user => user.UserId == userId)
                .Select(user => user.Role).SingleOrDefaultAsync(ct);

        // Preserve support for direct construction and non-EF Identity stores.
        var user = users.SupportsQueryableUsers
            ? await users.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct)
            : await users.FindByIdAsync(userId.ToString());
        ct.ThrowIfCancellationRequested();
        if (user is null || user.AccountStatus != AccountStatus.Active) return null;

        var roles = await users.GetRolesAsync(user);
        ct.ThrowIfCancellationRequested();
        return roles.Count == 1 && AppRoles.All.Contains(roles[0], StringComparer.Ordinal) ? roles[0] : null;
    }

    public async Task<bool> IsActiveRoleAsync(Guid userId, string role, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!AppRoles.All.Contains(role, StringComparer.Ordinal)) return false;
        return string.Equals(await GetActiveRoleAsync(userId, ct), role, StringComparison.Ordinal);
    }

    public Task<bool> IsActiveStudentAsync(Guid userId, CancellationToken ct = default) =>
        IsActiveRoleAsync(userId, AppRoles.Student, ct);

    public Task<bool> IsActiveTeacherAsync(Guid userId, CancellationToken ct = default) =>
        IsActiveRoleAsync(userId, AppRoles.Teacher, ct);

    public Task<bool> IsActiveAdminAsync(Guid userId, CancellationToken ct = default) =>
        IsActiveRoleAsync(userId, AppRoles.Admin, ct);
}
