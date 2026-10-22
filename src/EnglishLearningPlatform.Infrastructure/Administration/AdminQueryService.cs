using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Administration;

public sealed class AdminQueryService(AppDbContext db) : IAdminQueryService
{
    private const int PageSize = 20;

    private Task<bool> IsAdminAsync(Guid actorId, CancellationToken ct) =>
        db.HasActiveRoleAsync(actorId, AppRoles.Admin, ct);

    private static string Search(string? search)
    {
        var value = (search ?? "").Trim();
        return value.Length > 120 ? value[..120] : value;
    }

    public async Task<AdminPage<AdminTransaction>?> TransactionsAsync(Guid actorId, string? search, PaymentStatus? status,
        int page, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct)) return null;
        var term = Search(search);
        var query = db.Orders.AsNoTracking().Select(o => new
        {
            OrderId = o.Id,
            StudentEmail = db.Users.Where(u => u.Id == o.StudentUserId).Select(u => u.Email).FirstOrDefault() ?? "",
            CourseTitle = o.Course.Title, o.Amount, o.Currency, o.CreatedAtUtc,
            Status = o.Payments.OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id).Select(p => (PaymentStatus?)p.Status).FirstOrDefault(),
            PaidAtUtc = o.Payments.OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id).Select(p => p.PaidAtUtc).FirstOrDefault()
        });
        if (term.Length > 0)
        {
            var isId = Guid.TryParse(term, out var orderId);
            query = query.Where(o => o.StudentEmail.Contains(term) || o.CourseTitle.Contains(term) || (isId && o.OrderId == orderId));
        }
        if (status.HasValue) query = query.Where(o => (o.Status ?? PaymentStatus.Pending) == status.Value);
        var total = await query.CountAsync(ct);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);
        var rows = await query.OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.OrderId)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        var items = rows.Select(o => new AdminTransaction(o.OrderId, o.StudentEmail, o.CourseTitle,
            o.Amount, o.Currency, o.CreatedAtUtc, o.Status, o.PaidAtUtc)).ToList();
        return new(items, term, page, pages, total);
    }

    public async Task<AdminPage<AdminUser>?> UsersAsync(Guid actorId, string? search, AccountStatus? status,
        int page, CancellationToken ct = default, string? role = null)
    {
        if (!await IsAdminAsync(actorId, ct)) return null;
        var term = Search(search);
        var users = db.Users.AsNoTracking();
        if (term.Length > 0) users = users.Where(u => u.FullName.Contains(term) || (u.Email != null && u.Email.Contains(term)));
        if (status.HasValue) users = users.Where(u => u.AccountStatus == status.Value);
        if (!string.IsNullOrEmpty(role)) users = users.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id &&
            db.Roles.Any(r => r.Id == ur.RoleId && r.Name == role)));
        var total = await users.CountAsync(ct);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);
        var rows = await users.OrderByDescending(u => u.CreatedAtUtc).ThenByDescending(u => u.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(u => new { u.Id, u.FullName, u.Email, u.AccountStatus, u.CreatedAtUtc }).ToListAsync(ct);
        var ids = rows.Select(u => u.Id).ToList();
        var roles = await (from ur in db.UserRoles.AsNoTracking()
                           join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                           where ids.Contains(ur.UserId)
                           select new { ur.UserId, r.Name }).ToListAsync(ct);
        var items = rows.Select(u => new AdminUser(u.Id, u.FullName, u.Email ?? "",
            string.Join(", ", roles.Where(r => r.UserId == u.Id).Select(r => r.Name).Order()), u.AccountStatus, u.CreatedAtUtc)).ToList();
        return new(items, term, page, pages, total);
    }

    public async Task<AdminPage<AdminAudit>?> AuditAsync(Guid actorId, string? search, int page, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(actorId, ct)) return null;
        var term = Search(search);
        var query = db.AuditLogs.AsNoTracking();
        if (term.Length > 0) query = query.Where(a => a.ActorSnapshot.Contains(term) ||
            (a.TargetSnapshot != null && a.TargetSnapshot.Contains(term)) || a.Action.Contains(term));
        var total = await query.CountAsync(ct);
        var pages = Math.Max(1, (total + PageSize - 1) / PageSize); page = Math.Clamp(page, 1, pages);
        var items = await query.OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).Select(a => new AdminAudit(a.ActorSnapshot, a.TargetSnapshot,
                a.Action, a.BeforeData, a.AfterData, a.OccurredAtUtc)).ToListAsync(ct);
        return new(items, term, page, pages, total);
    }
}
