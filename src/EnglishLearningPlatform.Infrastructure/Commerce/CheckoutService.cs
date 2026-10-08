using System.Text.RegularExpressions;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Commerce;

public sealed class CheckoutService(AppDbContext db, UserManager<ApplicationUser> users,
    ILearningAccessService access, IPaymentGateway gateway, CheckoutTransactionExecutor transactions) : ICheckoutService
{
    private async Task<bool> ActiveStudent(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user?.AccountStatus != AccountStatus.Active) return false;
        var roles = await users.GetRolesAsync(user);
        return roles.Count == 1 && roles[0] == AppRoles.Student;
    }

    private async Task<bool> CanLearn(Guid userId, Guid courseId, CancellationToken ct) =>
        await access.CheckAccessAsync(userId, LearningResourceType.Course, courseId, LearningOperation.ViewContent, ct)
            == LearningAccessResult.Allowed;

    private async Task<CheckoutOutcome<CourseCheckout>> PreviewCore(Guid userId, Guid courseId, CancellationToken ct)
    {
        if (!await ActiveStudent(userId)) return new(CheckoutCode.Forbidden);
        var course = await db.Courses.AsNoTracking().SingleOrDefaultAsync(c => c.Id == courseId, ct);
        if (course is null) return new(CheckoutCode.NotFound);
        if (await CanLearn(userId, courseId, ct)) return new(CheckoutCode.AlreadyEnrolled);
        if (course.Status != CourseStatus.Published || !course.IsPaid || !CourseRules.ValidPrice(true, course.Price)
            || !await db.Users.AnyAsync(u => u.Id == course.OwnerTeacherUserId && u.AccountStatus != AccountStatus.Disabled, ct))
            return new(CheckoutCode.NotPurchasable);
        var teacher = await db.Users.Where(u => u.Id == course.OwnerTeacherUserId).Select(u => u.FullName).SingleAsync(ct);
        return new(CheckoutCode.Allowed, new(course.Id, course.Title, teacher, course.Price, "VND", gateway.IsEnabled));
    }

    public Task<CheckoutOutcome<CourseCheckout>> PreviewAsync(Guid userId, Guid courseId, CancellationToken ct = default) =>
        PreviewCore(userId, courseId, ct);

    // Existing schema is enough. A transaction-scoped SQL application lock serializes local commerce
    // across requests/processes. Deliberately coarse for this small sandbox; do not use an in-memory lock.
    private Task LockCommerce(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource = N'ELP.LocalCommerce',
            @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @result < 0 THROW 51000, 'Commerce lock unavailable.', 1;
        """, ct);

    public async Task<CheckoutOutcome<OrderCheckout>> CreateOrderAsync(Guid userId, Guid courseId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!gateway.IsEnabled)
        {
            return await transactions.ReadAsync("create-order-disabled", courseId, async () =>
            {
                var preview = await PreviewCore(userId, courseId, ct);
                return new CheckoutOutcome<OrderCheckout>(preview.Code == CheckoutCode.Allowed ? CheckoutCode.Disabled : preview.Code);
            }, new(CheckoutCode.IntegrationError), ct);
        }
        return await transactions.ExecuteAsync<CheckoutOutcome<OrderCheckout>>("create-order", courseId, async () =>
        {
            await LockCommerce(ct);
            var preview = await PreviewCore(userId, courseId, ct);
            if (preview.Code != CheckoutCode.Allowed) return new(preview.Code);
            var quote = preview.Value!;
            // Repeated clicks reuse the same unprocessed quote. Failed/cancelled attempts get a new Order.
            var order = await db.Orders.Where(o => o.StudentUserId == userId && o.CourseId == courseId
                && o.Amount == quote.Amount && o.Currency == quote.Currency && !o.Payments.Any())
                .OrderBy(o => o.CreatedAtUtc).ThenBy(o => o.Id).FirstOrDefaultAsync(ct);
            if (order is null)
            {
                order = new Order { StudentUserId = userId, CourseId = courseId, Amount = quote.Amount, Currency = quote.Currency };
                db.Orders.Add(order);
                await db.SaveChangesAsync(ct);
            }
            return new(CheckoutCode.Allowed, new(order.Id, courseId, quote.Title, quote.Teacher, order.Amount,
                order.Currency, null, false, true));
        }, result => result.Code == CheckoutCode.Allowed, new(CheckoutCode.IntegrationError), ct);
    }

    public async Task<CheckoutOutcome<OrderCheckout>> GetOrderAsync(Guid userId, Guid orderId, CancellationToken ct = default)
    {
        if (!await ActiveStudent(userId)) return new(CheckoutCode.Forbidden);
        // Filter by owner before returning any order metadata; another student's Order is a 404.
        var order = await db.Orders.AsNoTracking().Where(o => o.Id == orderId && o.StudentUserId == userId)
            .Select(o => new { o.Id, o.CourseId, o.Course.Title, o.Course.OwnerTeacherUserId, o.Amount, o.Currency,
                Status = o.Payments.OrderByDescending(p => p.CreatedAtUtc).Select(p => (PaymentStatus?)p.Status).FirstOrDefault() })
            .SingleOrDefaultAsync(ct);
        if (order is null) return new(CheckoutCode.NotFound);
        var teacher = await db.Users.Where(u => u.Id == order.OwnerTeacherUserId).Select(u => u.FullName).SingleAsync(ct);
        return new(CheckoutCode.Allowed, new(order.Id, order.CourseId, order.Title, teacher, order.Amount, order.Currency,
            order.Status, await CanLearn(userId, order.CourseId, ct), gateway.IsEnabled));
    }

    public async Task<CheckoutCode> SimulateAsync(Guid userId, Guid orderId, SandboxScenario scenario, CancellationToken ct = default)
    {
        if (!gateway.IsEnabled) return CheckoutCode.Disabled;
        if (!Enum.IsDefined(scenario)) return CheckoutCode.InvalidCallback;
        var owned = await GetOrderAsync(userId, orderId, ct);
        if (owned.Code != CheckoutCode.Allowed) return owned.Code;
        // Revalidate eligibility before starting a new simulation, but allow exact replays of terminal orders.
        if (!owned.Value!.Status.HasValue)
        {
            var preview = await PreviewCore(userId, owned.Value.CourseId, ct);
            if (preview.Code != CheckoutCode.Allowed) return preview.Code;
        }
        // Browser selects a demo scenario, not a Payment status/signature. The server gateway builds
        // and authenticates the event; HTTP webhook uses this exact same processing method.
        return await ProcessCallbackAsync(gateway.CreateCallback(owned.Value, scenario), ct);
    }

    private static bool Matches(Payment payment, PaymentCallback data) => payment.OrderId == data.OrderId
        && payment.Amount == data.Amount && payment.Status == data.Status && payment.Provider == data.Provider
        && payment.ProviderTransactionId == data.ProviderTransactionId && payment.Order.Currency == data.Currency;

    public async Task<CheckoutCode> ProcessCallbackAsync(SignedPaymentCallback callback, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!gateway.IsEnabled) return CheckoutCode.Disabled;
        if (!gateway.Verify(callback)) return CheckoutCode.InvalidCallback;
        var data = callback.Data;
        if (data.OrderId == Guid.Empty || data.Provider != LocalSandboxGateway.ProviderName || data.Currency != "VND"
            || !CourseRules.ValidPrice(true, data.Amount) || string.IsNullOrEmpty(data.ProviderTransactionId)
            || !Regex.IsMatch(data.ProviderTransactionId, "\\A[A-Za-z0-9._:-]{1,128}\\z")
            || data.Status is not (PaymentStatus.Succeeded or PaymentStatus.Failed or PaymentStatus.Cancelled))
            return CheckoutCode.InvalidCallback;

        return await transactions.ExecuteAsync("payment-callback", data.OrderId, async () =>
        {
            await LockCommerce(ct);
            var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == data.OrderId, ct);
            if (order is null || order.Amount != data.Amount || order.Currency != data.Currency) return CheckoutCode.InvalidCallback;
            var existing = await db.Payments.Include(p => p.Order).SingleOrDefaultAsync(p => p.ProviderTransactionId == data.ProviderTransactionId, ct);
            if (existing is not null)
            {
                if (!Matches(existing, data)) return CheckoutCode.Conflict;
                if (data.Status == PaymentStatus.Succeeded) await EnsureEnrollment(order, existing, ct);
                Audit(data, "Payment.CallbackReplay");
                await db.SaveChangesAsync(ct);
                return CheckoutCode.Allowed;
            }
            // First terminal event wins, even if a later event has a different transaction ID.
            if (await db.Payments.AnyAsync(p => p.OrderId == order.Id, ct)) return CheckoutCode.Conflict;

            var payment = new Payment { OrderId = order.Id, Amount = data.Amount, Status = data.Status,
                Provider = data.Provider, ProviderTransactionId = data.ProviderTransactionId,
                PaidAtUtc = data.Status == PaymentStatus.Succeeded ? DateTimeOffset.UtcNow : null };
            db.Payments.Add(payment);
            // Save Payment first, inside the same transaction, so failure while saving Enrollment rolls it back.
            await db.SaveChangesAsync(ct);
            if (data.Status == PaymentStatus.Succeeded)
                await EnsureEnrollment(order, payment, ct);
            Audit(data, "Payment.CallbackAccepted");
            await db.SaveChangesAsync(ct);
            return CheckoutCode.Allowed;
        }, result => result == CheckoutCode.Allowed, CheckoutCode.IntegrationError, ct);
    }

    private Task<bool> ValidEnrollment(Order order, CancellationToken ct) => db.Enrollments.AsNoTracking().AnyAsync(e =>
        e.StudentUserId == order.StudentUserId && e.CourseId == order.CourseId &&
        (!e.PaymentId.HasValue || (e.Payment!.Status == PaymentStatus.Succeeded &&
            e.Payment.Order.StudentUserId == order.StudentUserId && e.Payment.Order.CourseId == order.CourseId &&
            e.Payment.Amount == e.Payment.Order.Amount && e.Payment.Order.Currency == "VND")), ct);

    private async Task EnsureEnrollment(Order order, Payment payment, CancellationToken ct)
    {
        var enrollment = await db.Enrollments.FromSqlInterpolated($"SELECT * FROM Enrollments WITH (UPDLOCK, HOLDLOCK) WHERE StudentUserId = {order.StudentUserId} AND CourseId = {order.CourseId}")
            .SingleOrDefaultAsync(ct);
        if (enrollment is null)
            db.Enrollments.Add(new Enrollment { StudentUserId = order.StudentUserId, CourseId = order.CourseId, PaymentId = payment.Id });
        else if (!await ValidEnrollment(order, ct))
            enrollment.PaymentId = payment.Id;
        // Preserve valid/free Enrollment IDs and progress independently of current account/course lifecycle.
    }

    private void Audit(PaymentCallback data, string action) => db.AuditLogs.Add(new AuditLog
    {
        ActorSnapshot = LocalSandboxGateway.ProviderName, Action = action,
        AfterData = $"Order={data.OrderId:N}; Status={data.Status}" // No signature/key/credential in audit.
    });
}
