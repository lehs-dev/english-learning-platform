using System.Text.Json;
using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Administration;

public sealed class AdminManagementService(AppDbContext db, UserManager<ApplicationUser> users, IAdminQueryService queries) : IAdminManagementService
{
    // Preserve direct construction used by existing consumers; DI supplies the query abstraction.
    public AdminManagementService(AppDbContext db, UserManager<ApplicationUser> users)
        : this(db, users, new AdminQueryService(db))
    {
    }

    private Task<bool> IsAdmin(Guid id, CancellationToken ct) =>
        db.HasActiveRoleAsync(id, AppRoles.Admin, ct);

    // All administrative writes serialize on the same SQL transaction lock, including concurrent
    // demotions, so the last-active-admin check cannot race with another administrative write.
    private Task LockAsync(CancellationToken ct) => db.AcquireAdminManagementLockAsync(ct);

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
            var remaining = await db.ActiveRoleAssignments().CountAsync(
                user => user.UserId != targetId && user.Role == AppRoles.Admin, ct);
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

    // Kept for callers of the original management contract; reads belong to the query service.
    public Task<AdminPage<AdminAudit>?> AuditAsync(Guid actorId, string? search, int page, CancellationToken ct = default) =>
        queries.AuditAsync(actorId, search, page, ct);
}
