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
    IWebhookPaymentGateway hosted, ISandboxPaymentGateway local, CheckoutTransactionExecutor transactions,
    VnpayGateway? vnpay = null) : IPaymentConfirmationService, IVnpayPaymentService
{
    public Guid? ReadVnpayReturn(IReadOnlyDictionary<string, string> query) => vnpay?.ReadOrderId(query);

    public async Task<VnpayIpnResult> ConfirmVnpayAsync(IReadOnlyDictionary<string, string> query, CancellationToken ct = default)
        => vnpay?.UsesIpn == true ? await ConfirmVerifiedVnpayAsync(query, ct) : new("99", "IPN mode is disabled");

    public async Task<CheckoutCode> ConfirmVnpayReturnAsync(Guid actorId, IReadOnlyDictionary<string, string> query, CancellationToken ct = default)
    {
        if (vnpay is null || vnpay.UsesIpn || !vnpay.IsConfigured) return CheckoutCode.Disabled;
        var id = vnpay.ReadOrderId(query);
        if (!id.HasValue) return CheckoutCode.InvalidCallback;
        if (!await activeUsers.IsActiveStudentAsync(actorId, ct)) return CheckoutCode.Forbidden;
        if (!await db.Orders.AnyAsync(o => o.Id == id.Value && o.StudentUserId == actorId, ct)) return CheckoutCode.NotFound;
        var result = await ConfirmVerifiedVnpayAsync(query, ct);
        return result.RspCode is "00" or "02" ? CheckoutCode.Allowed : CheckoutCode.InvalidCallback;
    }

    private async Task<VnpayIpnResult> ConfirmVerifiedVnpayAsync(IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        if (vnpay is null || !vnpay.IsConfigured || hosted.Provider != vnpay.Provider)
            return new("99", "Payment service unavailable");
        if (!vnpay.ReadOrderId(query).HasValue) return new("97", "Invalid signature or merchant reference");
        var notification = vnpay.ReadNotification(query);
        if (notification is null) return new("99", "Invalid or unresolved payment data");
        try
        {
            // Read only after signature verification. Settlement rechecks under the commerce lock.
            var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == notification.OrderId, ct);
            if (order is null) return new("01", "Order not found");
            if (order.Provider != vnpay.Provider || order.Currency != "VND" || VnpayGateway.QuoteAmount(order.Amount) != notification.Amount)
                return new("04", "Invalid amount or provider");
            // Failure callbacks can lack PayDate; no PaidAtUtc is created for these receipts.
            if (notification.OccurredAtUtc == default)
                notification = notification with { OccurredAtUtc = order.CreatedAtUtc };
            var result = await ConfirmCore(notification, vnpay, false, ct, reportReplay: true);
            return result switch
            {
                PaymentConfirmation.Accepted => new("00", "Confirm Success"),
                PaymentConfirmation.AlreadyConfirmed => new("02", "Order already confirmed"),
                PaymentConfirmation.NotFound => new("01", "Order not found"),
                _ => new("99", "Payment could not be confirmed")
            };
        }
        catch (System.Data.Common.DbException) { return new("99", "Please retry later"); }
        catch (DbUpdateException) { return new("99", "Please retry later"); }
    }

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

    private Task<PaymentConfirmation> ConfirmCore(PaymentNotification notification, IPaymentGateway gateway, bool isLocal, CancellationToken ct, bool reportReplay = false) =>
        transactions.ExecuteAsync("payment-callback", notification.OrderId, async () =>
        {
            await db.AcquireCommerceLockAsync(ct);
            var courseId = await db.Orders.Where(o => o.Id == notification.OrderId).Select(o => (Guid?)o.CourseId).SingleOrDefaultAsync(ct);
            if (!courseId.HasValue) return PaymentConfirmation.NotFound;
            var course = await db.CoursesForUpdate(courseId.Value).SingleOrDefaultAsync(ct);
            var order = await db.Orders.SingleAsync(o => o.Id == notification.OrderId, ct);
            // Recover legacy fractional VND quotes only from an authenticated VNPay receipt
            // matching the exact whole-dong quote, never an arbitrary amount difference.
            if (gateway is VnpayGateway && order.Provider == gateway.Provider && order.Currency == "VND" &&
                order.Amount != VnpayGateway.QuoteAmount(order.Amount) &&
                notification.Amount == VnpayGateway.QuoteAmount(order.Amount) &&
                !await db.Payments.AnyAsync(p => p.OrderId == order.Id, ct))
            {
                db.AuditLogs.Add(new AuditLog { Action = "Payment.VnpayWholeDongQuote", ActorSnapshot = gateway.Provider,
                    TargetUserId = order.StudentUserId, BeforeData = $"Order={order.Id:D};Amount={order.Amount}",
                    AfterData = $"Order={order.Id:D};Amount={notification.Amount}" });
                order.Amount = notification.Amount;
            }
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
                return reportReplay ? PaymentConfirmation.AlreadyConfirmed : PaymentConfirmation.Accepted;
            }
            if (await db.Payments.AnyAsync(p => p.OrderId == order.Id && (isLocal || reportReplay || p.Status == PaymentStatus.Succeeded), ct))
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
        }, result => result is PaymentConfirmation.Accepted or PaymentConfirmation.AlreadyConfirmed, PaymentConfirmation.Unavailable, ct);
    private void Audit(Order order, Payment payment, string action, bool eligible) => db.AuditLogs.Add(new AuditLog
    {
        Action = action, ActorSnapshot = payment.Provider, TargetUserId = order.StudentUserId,
        AfterData = $"Order={order.Id:D};Payment={payment.Id:D};Status={payment.Status};EnrollmentEligible={eligible}"
    });
}
