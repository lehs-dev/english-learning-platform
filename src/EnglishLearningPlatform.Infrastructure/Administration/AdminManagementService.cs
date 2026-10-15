using System.Text.Json;
using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Administration;

public sealed class AdminManagementService(AppDbContext db, UserManager<ApplicationUser> users) : IAdminManagementService
{
    private Task<bool> IsAdmin(Guid id, CancellationToken ct) => db.Users.AnyAsync(u => u.Id == id &&
        u.AccountStatus == AccountStatus.Active && db.UserRoles.Count(r => r.UserId == id) == 1 &&
        db.UserRoles.Any(ur => ur.UserId == id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Admin)), ct);

    // All administrative writes serialize on the same SQL transaction lock, including concurrent
    // demotions, so the last-active-admin check cannot race with another administrative write.
    private Task LockAsync(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        DECLARE @result int;
        EXEC @result = sp_getapplock @Resource=N'ELP.AdminManagement', @LockMode='Exclusive',
            @LockOwner='Transaction', @LockTimeout=10000;
        IF @result < 0 THROW 51000, 'Cannot acquire administration lock.', 1;
        """, ct);

    private void Audit(ApplicationUser actor, ApplicationUser target, string action, string? before, string after) =>
        db.AuditLogs.Add(new AuditLog { ActorUserId = actor.Id, TargetUserId = target.Id,
            ActorSnapshot = actor.Email ?? actor.Id.ToString(), TargetSnapshot = target.Email ?? target.Id.ToString(),
            Action = action, BeforeData = before, AfterData = after });

    public async Task<AdminChangeResult> CreateTeacherAsync(Guid actorId, string name, string email, string password, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        if (!await IsAdmin(actorId, ct)) return new(false, "Bạn không có quyền quản trị.");
        name = (name ?? "").Trim(); email = (email ?? "").Trim();
        if (name.Length is < 2 or > 200 || email.Length > 256) return new(false, "Họ tên hoặc email không hợp lệ.");
        var target = new ApplicationUser { FullName = name, Email = email, UserName = email };
        var created = await users.CreateAsync(target, password);
        if (!created.Succeeded) return new(false, "Không tạo được tài khoản: email có thể đã tồn tại hoặc mật khẩu chưa đáp ứng yêu cầu.");
        var assigned = await users.AddToRoleAsync(target, AppRoles.Teacher);
        if (!assigned.Succeeded) return new(false, "Không gán được vai trò Teacher.");
        var actor = await db.Users.SingleAsync(u => u.Id == actorId, ct);
        Audit(actor, target, "CreateTeacher", null, JsonSerializer.Serialize(new { Role = AppRoles.Teacher, Status = target.AccountStatus.ToString() }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(true);
    }

    public async Task<AdminChangeResult> UpdateUserAsync(Guid actorId, Guid targetId, string role, AccountStatus status, CancellationToken ct = default)
    {
        if (!AppRoles.All.Contains(role, StringComparer.Ordinal) || !Enum.IsDefined(status)) return new(false, "Vai trò hoặc trạng thái không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        if (!await IsAdmin(actorId, ct)) return new(false, "Bạn không có quyền quản trị.");
        var target = await users.FindByIdAsync(targetId.ToString());
        if (target is null) return new(false, "Không tìm thấy tài khoản.");
        var roles = await users.GetRolesAsync(target);
        if (roles.Count != 1) return new(false, "Tài khoản phải có đúng một vai trò trước khi cập nhật.");
        if (roles[0] == role && target.AccountStatus == status &&
            (status != AccountStatus.Active || target.LockoutEnd is null && target.AccessFailedCount == 0)) return new(true);
        if (roles[0] == AppRoles.Admin && target.AccountStatus == AccountStatus.Active && (role != AppRoles.Admin || status != AccountStatus.Active))
        {
            var remaining = await db.Users.CountAsync(u => u.Id != targetId && u.AccountStatus == AccountStatus.Active &&
                db.UserRoles.Count(r => r.UserId == u.Id) == 1 && db.UserRoles.Any(ur => ur.UserId == u.Id &&
                    db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Admin)), ct);
            if (remaining == 0) return new(false, "Không thể khóa, vô hiệu hóa hoặc đổi vai trò Admin hoạt động cuối cùng.");
        }
        var before = JsonSerializer.Serialize(new { Role = roles[0], Status = target.AccountStatus.ToString(), target.LockoutEnd, target.AccessFailedCount });
        if (roles[0] != role)
        {
            if (!(await users.RemoveFromRolesAsync(target, roles)).Succeeded || !(await users.AddToRoleAsync(target, role)).Succeeded)
                return new(false, "Không cập nhật được vai trò.");
        }
        target.AccountStatus = status;
        target.UpdatedAtUtc = DateTimeOffset.UtcNow;
        // Unlock also clears Identity's failed-login lockout, rather than just the business status.
        if (status == AccountStatus.Active) { target.LockoutEnd = null; target.AccessFailedCount = 0; }
        if (!(await users.UpdateAsync(target)).Succeeded) return new(false, "Không cập nhật được tài khoản.");
        var actor = await db.Users.SingleAsync(u => u.Id == actorId, ct);
        Audit(actor, target, "UpdateUser", before, JsonSerializer.Serialize(new { Role = role, Status = status.ToString(), target.LockoutEnd, target.AccessFailedCount }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(true);
    }

    public async Task<AdminPage<AdminAudit>?> AuditAsync(Guid actorId, string? search, int page, CancellationToken ct = default)
    {
        if (!await IsAdmin(actorId, ct)) return null;
        var term = (search ?? "").Trim(); if (term.Length > 120) term = term[..120];
        var query = db.AuditLogs.AsNoTracking();
        if (term.Length > 0) query = query.Where(a => a.ActorSnapshot.Contains(term) ||
            (a.TargetSnapshot != null && a.TargetSnapshot.Contains(term)) || a.Action.Contains(term));
        var total = await query.CountAsync(ct);
        var pages = Math.Max(1, (total + 19) / 20); page = Math.Clamp(page, 1, pages);
        var items = await query.OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id)
            .Skip((page - 1) * 20).Take(20).Select(a => new AdminAudit(a.ActorSnapshot, a.TargetSnapshot,
                a.Action, a.BeforeData, a.AfterData, a.OccurredAtUtc)).ToListAsync(ct);
        return new(items, term, page, pages, total);
    }
}
