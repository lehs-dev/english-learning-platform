using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Administration;

public sealed class AdminDashboardQueryService(AppDbContext db) : IAdminDashboardQueryService
{
    public async Task<AdminDashboardSummary?> GetAsync(Guid actorId, CancellationToken ct = default)
    {
        // Service authorization also protects callers outside MVC.
        if (!await db.HasActiveRoleAsync(actorId, AppRoles.Admin, ct)) return null;

        var users = db.Users.AsNoTracking();
        var courses = db.Courses.AsNoTracking();
        var enrollments = db.Enrollments.AsNoTracking();
        var orders = db.Orders.AsNoTracking();

        // Await sequentially: a scoped DbContext cannot run concurrent operations.
        var totalUsers = await users.CountAsync(ct);
        var locked = await users.CountAsync(u => u.AccountStatus == AccountStatus.Locked, ct);
        var disabled = await users.CountAsync(u => u.AccountStatus == AccountStatus.Disabled, ct);
        var students = await users.CountAsync(u => db.UserRoles.Any(ur => ur.UserId == u.Id &&
            db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Student)), ct);
        var teachers = await users.CountAsync(u => db.UserRoles.Any(ur => ur.UserId == u.Id &&
            db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Teacher)), ct);
        var admins = await users.CountAsync(u => db.UserRoles.Any(ur => ur.UserId == u.Id &&
            db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Admin)), ct);
        var totalCourses = await courses.CountAsync(ct);
        var byStatus = await courses.GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        var totalEnrollments = await enrollments.CountAsync(ct);
        var completed = await enrollments.CountAsync(e => e.CompletedAtUtc.HasValue, ct);
        var totalOrders = await orders.CountAsync(ct);
        // Same success precedence as CheckoutOrderService; EXISTS avoids counting retry rows twice.
        var succeeded = await orders.CountAsync(o => o.Payments.Any(p => p.Status == PaymentStatus.Succeeded), ct);

        return new(totalUsers, locked, disabled, students, teachers, totalCourses,
            byStatus.GetValueOrDefault(CourseStatus.Published), byStatus.GetValueOrDefault(CourseStatus.Draft),
            totalEnrollments, completed, totalOrders, succeeded,
            byStatus.GetValueOrDefault(CourseStatus.Unpublished), byStatus.GetValueOrDefault(CourseStatus.Archived), admins);
    }
}
