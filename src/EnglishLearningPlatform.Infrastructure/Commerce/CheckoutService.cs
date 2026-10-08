using System.Data;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Commerce;

public sealed class CheckoutService(AppDbContext db, UserManager<ApplicationUser> users, IPaymentGateway gateway) : ICheckoutService
{
    private async Task<bool> ActiveStudent(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || user.AccountStatus != AccountStatus.Active) return false;
        var roles = await users.GetRolesAsync(user);
        return roles.Count == 1 && roles[0] == AppRoles.Student;
    }

    private Task<Course?> LockCourse(Guid id, CancellationToken ct) => db.Courses
        .FromSqlInterpolated($"SELECT * FROM Courses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}").SingleOrDefaultAsync(ct);

    public async Task<CheckoutStart> StartAsync(Guid userId, Guid courseId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var course = await LockCourse(courseId, ct);
        if (!await ActiveStudent(userId)) return new(LearningAccessResult.Forbidden, courseId);
        if (course is null) return new(LearningAccessResult.NotFound, courseId);
        if (await db.Enrollments.Valid().AnyAsync(e => e.StudentUserId == userId && e.CourseId == courseId, ct))
            return new(LearningAccessResult.Allowed, courseId, AlreadyEnrolled: true);
        if (course.Status != CourseStatus.Published) return new(LearningAccessResult.NotFound, courseId);
        if (!course.IsPaid || !CourseRules.ValidPrice(true, course.Price) ||
            !await db.Users.AnyAsync(u => u.Id == course.OwnerTeacherUserId && u.AccountStatus != AccountStatus.Disabled, ct))
            return new(LearningAccessResult.Forbidden, courseId);
        if (await db.Enrollments.AnyAsync(e => e.StudentUserId == userId && e.CourseId == courseId, ct))
            return new(LearningAccessResult.Forbidden, courseId);
        if (!gateway.IsConfigured) return new(LearningAccessResult.Allowed, courseId, Error: "Thanh toán hiện chưa khả dụng. Vui lòng thử lại sau.");
        var now = DateTimeOffset.UtcNow;
        var order = await db.Orders.Where(o => o.StudentUserId == userId && o.CourseId == courseId &&
            o.Amount == course.Price && o.Currency == "VND" && o.Provider == gateway.Provider &&
            o.ExpiresAtUtc > now && !o.Payments.Any()).OrderByDescending(o => o.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (order is null)
        {
            order = new Order { StudentUserId = userId, CourseId = courseId, Amount = course.Price,
                Provider = gateway.Provider, ExpiresAtUtc = gateway.CheckoutDeadline };
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
        }
        var url = gateway.CreateCheckoutUrl(new(order.Id, order.Amount, order.Currency, order.ExpiresAtUtc!.Value));
        await tx.CommitAsync(ct);
        return new(LearningAccessResult.Allowed, courseId, order.Id, url);
    }

    public async Task<LearningOutcome<CheckoutPage>> GetAsync(Guid userId, Guid orderId, CancellationToken ct = default)
    {
        if (!await ActiveStudent(userId)) return new(LearningAccessResult.Forbidden);
        var order = await db.Orders.AsNoTracking().Include(o => o.Course).Include(o => o.Payments)
            .SingleOrDefaultAsync(o => o.Id == orderId && o.StudentUserId == userId, ct);
        if (order is null) return new(LearningAccessResult.NotFound);
        var enrolled = await db.Enrollments.Valid().AnyAsync(e => e.StudentUserId == userId && e.CourseId == order.CourseId, ct);
        var status = order.Payments.Any(p => p.Status == PaymentStatus.Succeeded) ? PaymentStatus.Succeeded :
            order.Payments.Any(p => p.Status == PaymentStatus.Failed) ? PaymentStatus.Failed : PaymentStatus.Pending;
        var canCheckout = !enrolled && status == PaymentStatus.Pending && order.ExpiresAtUtc > DateTimeOffset.UtcNow &&
            order.Course.Status == CourseStatus.Published && order.Course.IsPaid && order.Amount == order.Course.Price &&
            gateway.IsConfigured && order.Provider == gateway.Provider &&
            await db.Users.AnyAsync(u => u.Id == order.Course.OwnerTeacherUserId && u.AccountStatus != AccountStatus.Disabled, ct);
        return new(LearningAccessResult.Allowed, new(order.Id, order.CourseId, order.Course.Title, order.Amount, order.Currency,
            status, enrolled, order.ExpiresAtUtc, canCheckout ? gateway.CreateCheckoutUrl(new(order.Id, order.Amount, order.Currency, order.ExpiresAtUtc!.Value)) : null));
    }

    public async Task<PaymentConfirmation> ConfirmAsync(byte[] body, string signature, CancellationToken ct = default)
    {
        if (!gateway.IsConfigured) return PaymentConfirmation.Unavailable;
        var notification = gateway.VerifyNotification(body, signature);
        if (notification is null) return PaymentConfirmation.Invalid;
        var courseId = await db.Orders.AsNoTracking().Where(o => o.Id == notification.OrderId).Select(o => (Guid?)o.CourseId).SingleOrDefaultAsync(ct);
        if (!courseId.HasValue) return PaymentConfirmation.NotFound;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // All enrollment, checkout and progress mutations take the course lock first.
        var course = await LockCourse(courseId.Value, ct);
        var order = await db.Orders.SingleAsync(o => o.Id == notification.OrderId, ct);
        if (order.Provider != gateway.Provider || order.Amount != notification.Amount || order.Currency != notification.Currency ||
            !order.ExpiresAtUtc.HasValue || notification.OccurredAtUtc < order.CreatedAtUtc.AddMinutes(-1) || notification.OccurredAtUtc > order.ExpiresAtUtc)
            return PaymentConfirmation.Invalid;
        var existing = await db.Payments.FromSqlInterpolated($"SELECT * FROM Payment WITH (UPDLOCK, HOLDLOCK) WHERE ProviderTransactionId = {notification.ProviderTransactionId}").SingleOrDefaultAsync(ct);
        if (existing is not null)
            return existing.OrderId == order.Id && existing.Provider == gateway.Provider && existing.Amount == notification.Amount && existing.Status == notification.Status
                ? PaymentConfirmation.Accepted : PaymentConfirmation.Conflict;
        // A completed order cannot be paid again under a different transaction id.
        if (await db.Payments.AnyAsync(p => p.OrderId == order.Id && p.Status == PaymentStatus.Succeeded, ct)) return PaymentConfirmation.Conflict;
        var payment = new Payment { OrderId = order.Id, Amount = notification.Amount, Provider = gateway.Provider,
            ProviderTransactionId = notification.ProviderTransactionId, Status = notification.Status,
            PaidAtUtc = notification.Status == PaymentStatus.Succeeded ? notification.OccurredAtUtc : null };
        db.Payments.Add(payment);
        // Record the verified receipt even if the course was withdrawn/account disabled while paying.
        // In that case no new learning rights are issued, and the result page explains the state.
        var canEnroll = notification.Status == PaymentStatus.Succeeded && course is not null &&
            course.Status == CourseStatus.Published && course.IsPaid && CourseRules.ValidPrice(true, course.Price) &&
            await ActiveStudent(order.StudentUserId) &&
            await db.Users.AnyAsync(u => u.Id == course.OwnerTeacherUserId && u.AccountStatus != AccountStatus.Disabled, ct);
        if (canEnroll && !await db.Enrollments.AnyAsync(e => e.StudentUserId == order.StudentUserId && e.CourseId == order.CourseId, ct))
            db.Enrollments.Add(new Enrollment { StudentUserId = order.StudentUserId, CourseId = order.CourseId, Payment = payment });
        db.AuditLogs.Add(new AuditLog { Action = "PaymentConfirmed", ActorSnapshot = gateway.Provider,
            TargetUserId = order.StudentUserId, AfterData = $"Order={order.Id:D};Payment={payment.Id:D};Status={payment.Status};EnrollmentEligible={canEnroll}" });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return PaymentConfirmation.Accepted;
    }
}
