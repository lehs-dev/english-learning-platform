using System.Text.RegularExpressions;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Commerce;

// Verifies provider notifications and settles a receipt/enrollment in one transaction.
public sealed class PaymentConfirmationService(AppDbContext db, IActiveUserValidator activeUsers,
    IWebhookPaymentGateway hosted, ISandboxPaymentGateway local, CheckoutTransactionExecutor transactions) : IPaymentConfirmationService
{
    public async Task<CheckoutCode> ProcessCallbackAsync(SignedPaymentCallback callback, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!local.IsConfigured) return CheckoutCode.Disabled;
        if (!local.Verify(callback)) return CheckoutCode.InvalidCallback;
        var data = callback.Data;
        if (data.Provider != local.Provider || data.OrderId == Guid.Empty || data.Currency != "VND" ||
            !CourseRules.ValidPrice(true, data.Amount) || string.IsNullOrEmpty(data.ProviderTransactionId) ||
            !Regex.IsMatch(data.ProviderTransactionId, "\\A[A-Za-z0-9._:-]{1,128}\\z") ||
            data.Status is not (PaymentStatus.Succeeded or PaymentStatus.Failed or PaymentStatus.Cancelled) || data.OccurredAtUtc == default)
            return CheckoutCode.InvalidCallback;
        var confirmation = await ConfirmCore(new(data.OrderId, data.ProviderTransactionId, data.Amount,
            data.Currency, data.Status, data.OccurredAtUtc), local, true, ct);
        return confirmation switch
        {
            PaymentConfirmation.Accepted => CheckoutCode.Allowed,
            PaymentConfirmation.Conflict => CheckoutCode.Conflict,
            PaymentConfirmation.Unavailable => CheckoutCode.IntegrationError,
            _ => CheckoutCode.InvalidCallback
        };
    }
    public async Task<PaymentConfirmation> ConfirmAsync(byte[] body, string signature, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!hosted.IsConfigured) return PaymentConfirmation.Unavailable;
        var notification = hosted.VerifyNotification(body, signature);
        return notification is null ? PaymentConfirmation.Invalid : await ConfirmCore(notification, hosted, false, ct);
    }

    private Task<PaymentConfirmation> ConfirmCore(PaymentNotification notification, IPaymentGateway gateway, bool isLocal, CancellationToken ct) =>
        transactions.ExecuteAsync("payment-callback", notification.OrderId, async () =>
        {
            await db.AcquireCommerceLockAsync(ct);
            var courseId = await db.Orders.Where(o => o.Id == notification.OrderId).Select(o => (Guid?)o.CourseId).SingleOrDefaultAsync(ct);
            if (!courseId.HasValue) return PaymentConfirmation.NotFound;
            var course = await db.CoursesForUpdate(courseId.Value).SingleOrDefaultAsync(ct);
            var order = await db.Orders.SingleAsync(o => o.Id == notification.OrderId, ct);
            if (order.Provider != gateway.Provider || order.Amount != notification.Amount || order.Currency != notification.Currency ||
                !order.ExpiresAtUtc.HasValue || notification.OccurredAtUtc < order.CreatedAtUtc.AddMinutes(-1) ||
                notification.OccurredAtUtc > order.ExpiresAtUtc || notification.OccurredAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
                return PaymentConfirmation.Invalid;
            var existing = await db.PaymentsForUpdate(notification.ProviderTransactionId)
                .SingleOrDefaultAsync(ct);
            var eligible = notification.Status == PaymentStatus.Succeeded && course is not null &&
                course.Status == CourseStatus.Published && course.IsPaid && CourseRules.ValidPrice(true, course.Price) &&
                await activeUsers.IsActiveStudentAsync(order.StudentUserId, ct) &&
                await db.Users.AnyAsync(u => u.Id == course.OwnerTeacherUserId && u.AccountStatus != AccountStatus.Disabled, ct);
            if (existing is not null)
            {
                if (existing.OrderId != order.Id || existing.Provider != gateway.Provider || existing.Amount != notification.Amount || existing.Status != notification.Status)
                    return PaymentConfirmation.Conflict;
                // Preserve local legacy repair, but hosted replay must not grant rights denied at first receipt.
                if (isLocal && eligible && !await db.Enrollments.AnyAsync(e => e.StudentUserId == order.StudentUserId && e.CourseId == order.CourseId, ct))
                    db.Enrollments.Add(new Enrollment { StudentUserId = order.StudentUserId, CourseId = order.CourseId, Payment = existing });
                if (isLocal)
                {
                    Audit(order, existing, "Payment.CallbackReplay", eligible);
                    await db.SaveChangesAsync(ct);
                }
                return PaymentConfirmation.Accepted;
            }
            if (await db.Payments.AnyAsync(p => p.OrderId == order.Id && (isLocal || p.Status == PaymentStatus.Succeeded), ct))
                return PaymentConfirmation.Conflict;
            var payment = new Payment { OrderId = order.Id, Amount = notification.Amount, Status = notification.Status,
                Provider = gateway.Provider, ProviderTransactionId = notification.ProviderTransactionId,
                PaidAtUtc = notification.Status == PaymentStatus.Succeeded ? notification.OccurredAtUtc : null };
            db.Payments.Add(payment); await db.SaveChangesAsync(ct);
            if (eligible && !await db.Enrollments.AnyAsync(e => e.StudentUserId == order.StudentUserId && e.CourseId == order.CourseId, ct))
                db.Enrollments.Add(new Enrollment { StudentUserId = order.StudentUserId, CourseId = order.CourseId, Payment = payment });
            Audit(order, payment, "PaymentConfirmed", eligible);
            await db.SaveChangesAsync(ct);
            return PaymentConfirmation.Accepted;
        }, result => result == PaymentConfirmation.Accepted, PaymentConfirmation.Unavailable, ct);
    private void Audit(Order order, Payment payment, string action, bool eligible) => db.AuditLogs.Add(new AuditLog
    {
        Action = action, ActorSnapshot = payment.Provider, TargetUserId = order.StudentUserId,
        AfterData = $"Order={order.Id:D};Payment={payment.Id:D};Status={payment.Status};EnrollmentEligible={eligible}"
    });
}
