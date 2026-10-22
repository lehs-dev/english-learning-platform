using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace EnglishLearningPlatform.Infrastructure.Identity;

internal static class ActiveUserQueries
{
    // Keep the current-account and exactly-one-role rules in one SQL-translatable query.
    // No tracking is used so transaction rechecks see status changes made elsewhere.
    internal static IQueryable<ActiveUserRole> ActiveRoleAssignments(this AppDbContext db) =>
        SelectActiveRoles(db.Users.AsNoTracking(), db.UserRoles, db.Roles);

    internal static IQueryable<ActiveUserRole> SelectActiveRoles(
        IQueryable<ApplicationUser> users, IQueryable<IdentityUserRole<Guid>> assignments,
        IQueryable<IdentityRole<Guid>> roles) =>
        from user in users
        where user.AccountStatus == AccountStatus.Active
            && assignments.Count(assignment => assignment.UserId == user.Id) == 1
        join assignment in assignments on user.Id equals assignment.UserId
        join role in roles on assignment.RoleId equals role.Id
        where role.Name != null && AppRoles.All.Contains(role.Name)
        select new ActiveUserRole { UserId = user.Id, Role = role.Name! };

    internal static Task<bool> HasActiveRoleAsync(
        this AppDbContext db, Guid userId, string role, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (userId == Guid.Empty || !AppRoles.All.Contains(role, StringComparer.Ordinal))
            return Task.FromResult(false);

        return db.ActiveRoleAssignments().AnyAsync(user => user.UserId == userId && user.Role == role, ct);
    }

    internal sealed record ActiveUserRole
    {
        public Guid UserId { get; init; }
        public string Role { get; init; } = string.Empty;
    }
}
