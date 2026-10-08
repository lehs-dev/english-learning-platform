using System.Text.RegularExpressions;
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

// One order/receipt/enrollment pipeline; hosted and local are deliberately separate adapters.
public sealed class CheckoutService(AppDbContext db, UserManager<ApplicationUser> users,
    IPaymentGateway hosted, LocalSandboxGateway local, CheckoutTransactionExecutor transactions) : ICheckoutService
{
    private IPaymentGateway PurchaseGateway => hosted.IsConfigured ? hosted : local.IsEnabled ? local : hosted;
    private async Task<bool> ActiveStudent(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user?.AccountStatus != AccountStatus.Active) return false;
        var roles = await users.GetRolesAsync(user);
        return roles.Count == 1 && roles[0] == AppRoles.Student;
    }
    private Task LockCommerce(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource = N'ELP.Commerce',
            @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @result < 0 THROW 51000, 'Commerce lock unavailable.', 1;
        """, ct);
    private Task<Course?> LockCourse(Guid id, CancellationToken ct) => db.Courses
        .FromSqlInterpolated($"SELECT * FROM Courses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}").SingleOrDefaultAsync(ct);
    private Task<bool> Enrolled(Guid user, Guid course, CancellationToken ct) => db.Enrollments.Valid()
        .AnyAsync(e => e.StudentUserId == user && e.CourseId == course, ct);
    private async Task<CheckoutCode> Eligibility(Guid user, Course? course, CancellationToken ct)
    {
        if (!await ActiveStudent(user)) return CheckoutCode.Forbidden;
        if (course is null) return CheckoutCode.NotFound;
        if (await Enrolled(user, course.Id, ct)) return CheckoutCode.AlreadyEnrolled;
        if (course.Status != CourseStatus.Published || !course.IsPaid || !CourseRules.ValidPrice(true, course.Price) ||
            !await db.Users.AnyAsync(u => u.Id == course.OwnerTeacherUserId && u.AccountStatus != AccountStatus.Disabled, ct))
            return CheckoutCode.NotPurchasable;
        // Adopt main's rule: do not sell again over an existing invalid enrollment.
        if (await db.Enrollments.AnyAsync(e => e.StudentUserId == user && e.CourseId == course.Id, ct)) return CheckoutCode.Forbidden;
        return CheckoutCode.Allowed;
    }
    public Task<CheckoutOutcome<CourseCheckout>> PreviewAsync(Guid userId, Guid courseId, CancellationToken ct = default) =>
        transactions.ReadAsync<CheckoutOutcome<CourseCheckout>>("checkout-preview", courseId, async () =>
        {
            var course = await db.Courses.AsNoTracking().SingleOrDefaultAsync(c => c.Id == courseId, ct);
            var code = await Eligibility(userId, course, ct);
            if (code != CheckoutCode.Allowed) return new(code);
            var teacher = await db.Users.Where(u => u.Id == course!.OwnerTeacherUserId).Select(u => u.FullName).SingleAsync(ct);
            return new(CheckoutCode.Allowed, new(courseId, course!.Title, teacher, course.Price, "VND", local.IsEnabled, hosted.IsConfigured));
        }, new(CheckoutCode.IntegrationError), ct);

    private Task<CheckoutOutcome<OrderCheckout>> StartCore(Guid userId, Guid courseId, IPaymentGateway gateway, CancellationToken ct) =>
        transactions.ExecuteAsync<CheckoutOutcome<OrderCheckout>>("create-order", courseId, async () =>
        {
            await LockCommerce(ct);
            var course = await LockCourse(courseId, ct);
            var code = await Eligibility(userId, course, ct);
            if (code != CheckoutCode.Allowed)
                return new(code == CheckoutCode.NotPurchasable && course is not null &&
                    course.Status != CourseStatus.Published && ReferenceEquals(gateway, hosted) ? CheckoutCode.NotFound : code);
            if (!gateway.IsConfigured) return new(CheckoutCode.Disabled);
            var now = DateTimeOffset.UtcNow;
            var order = await db.Orders.Where(o => o.StudentUserId == userId && o.CourseId == courseId &&
                o.Amount == course!.Price && o.Currency == "VND" && o.Provider == gateway.Provider &&
                o.ExpiresAtUtc > now && !o.Payments.Any()).OrderByDescending(o => o.CreatedAtUtc).ThenBy(o => o.Id).FirstOrDefaultAsync(ct);
            if (order is null)
            {
                order = new Order { StudentUserId = userId, CourseId = courseId, Amount = course!.Price,
                    Provider = gateway.Provider, ExpiresAtUtc = gateway.CheckoutDeadline };
                db.Orders.Add(order); await db.SaveChangesAsync(ct);
            }
            var teacher = await db.Users.Where(u => u.Id == course!.OwnerTeacherUserId).Select(u => u.FullName).SingleAsync(ct);
            var url = gateway.CreateCheckoutUrl(new(order.Id, order.Amount, order.Currency, order.ExpiresAtUtc!.Value));
            return new(CheckoutCode.Allowed, new(order.Id, courseId, course!.Title, teacher, order.Amount, order.Currency,
                null, false, local.IsEnabled, order.Provider, order.ExpiresAtUtc, order.CreatedAtUtc, url));
        }, result => result.Code == CheckoutCode.Allowed, new(CheckoutCode.IntegrationError), ct);

    public Task<CheckoutOutcome<OrderCheckout>> CreateOrderAsync(Guid userId, Guid courseId, CancellationToken ct = default) =>
        StartCore(userId, courseId, PurchaseGateway, ct);
    public async Task<CheckoutStart> StartAsync(Guid userId, Guid courseId, CancellationToken ct = default)
    {
        // Existing hosted Start route never falls back to a simulated payment.
        var result = await StartCore(userId, courseId, hosted, ct);
        return result.Code switch
        {
            CheckoutCode.Allowed => new(LearningAccessResult.Allowed, courseId, result.Value!.Id, result.Value.CheckoutUrl),
            CheckoutCode.AlreadyEnrolled => new(LearningAccessResult.Allowed, courseId, AlreadyEnrolled: true),
            CheckoutCode.NotFound => new(LearningAccessResult.NotFound, courseId),
            CheckoutCode.NotPurchasable => new(LearningAccessResult.Forbidden, courseId),
            CheckoutCode.Forbidden => new(LearningAccessResult.Forbidden, courseId),
            _ => new(LearningAccessResult.Allowed, courseId, Error: "Thanh toán hiện chưa khả dụng. Vui lòng thử lại sau.")
        };
    }

    public Task<CheckoutOutcome<OrderCheckout>> GetOrderAsync(Guid userId, Guid orderId, CancellationToken ct = default) =>
        transactions.ReadAsync<CheckoutOutcome<OrderCheckout>>("get-order", orderId, async () =>
        {
            if (!await ActiveStudent(userId)) return new(CheckoutCode.Forbidden);
            var order = await db.Orders.AsNoTracking().Include(o => o.Course).Include(o => o.Payments)
                .SingleOrDefaultAsync(o => o.Id == orderId && o.StudentUserId == userId, ct);
            if (order is null) return new(CheckoutCode.NotFound);
            var enrolled = await Enrolled(userId, order.CourseId, ct);
            PaymentStatus? status = order.Payments.Any(p => p.Status == PaymentStatus.Succeeded) ? PaymentStatus.Succeeded :
                order.Payments.Any(p => p.Status == PaymentStatus.Failed) ? PaymentStatus.Failed :
                order.Payments.Any(p => p.Status == PaymentStatus.Cancelled) ? PaymentStatus.Cancelled : null;
            var isLocal = order.Provider == LocalSandboxGateway.ProviderName;
            var gateway = isLocal ? (IPaymentGateway)local : hosted;
            var canCheckout = !enrolled && !status.HasValue && order.ExpiresAtUtc > DateTimeOffset.UtcNow &&
                order.Course.Status == CourseStatus.Published && order.Course.IsPaid && order.Amount == order.Course.Price &&
                gateway.IsConfigured && order.Provider == gateway.Provider &&
                await db.Users.AnyAsync(u => u.Id == order.Course.OwnerTeacherUserId && u.AccountStatus != AccountStatus.Disabled, ct);
            var teacher = await db.Users.Where(u => u.Id == order.Course.OwnerTeacherUserId).Select(u => u.FullName).SingleAsync(ct);
            return new(CheckoutCode.Allowed, new(order.Id, order.CourseId, order.Course.Title, teacher, order.Amount,
                order.Currency, status, enrolled && order.Course.Status != CourseStatus.Draft, local.IsEnabled,
                order.Provider, order.ExpiresAtUtc, order.CreatedAtUtc,
                canCheckout ? gateway.CreateCheckoutUrl(new(order.Id, order.Amount, order.Currency, order.ExpiresAtUtc!.Value)) : null, canCheckout));
        }, new(CheckoutCode.IntegrationError), ct);
    public async Task<LearningOutcome<CheckoutPage>> GetAsync(Guid userId, Guid orderId, CancellationToken ct = default)
    {
        var result = await GetOrderAsync(userId, orderId, ct);
        if (result.Code != CheckoutCode.Allowed) return new(result.Code == CheckoutCode.NotFound ? LearningAccessResult.NotFound : LearningAccessResult.Forbidden);
        var order = result.Value!;
        return new(LearningAccessResult.Allowed, new(order.Id, order.CourseId, order.Title, order.Amount, order.Currency,
            order.Status ?? PaymentStatus.Pending, order.CanLearn, order.ExpiresAtUtc, order.CheckoutUrl));
    }
    public async Task<CheckoutCode> SimulateAsync(Guid userId, Guid orderId, SandboxScenario scenario, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!local.IsEnabled) return CheckoutCode.Disabled;
        if (!Enum.IsDefined(scenario)) return CheckoutCode.InvalidCallback;
        var owned = await GetOrderAsync(userId, orderId, ct);
        if (owned.Code != CheckoutCode.Allowed) return owned.Code;
        // A local key/scenario can never sign or settle an externally hosted Order.
        if (!owned.Value!.IsSandbox) return CheckoutCode.Forbidden;
        if (!owned.Value.Status.HasValue && !owned.Value.CanCheckout) return CheckoutCode.NotPurchasable;
        if (owned.Value.CanLearn && !owned.Value.Status.HasValue) return CheckoutCode.AlreadyEnrolled;
        return await ProcessCallbackAsync(local.CreateCallback(owned.Value, scenario), ct);
    }
    public async Task<CheckoutCode> ProcessCallbackAsync(SignedPaymentCallback callback, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!local.IsEnabled) return CheckoutCode.Disabled;
        if (!local.Verify(callback)) return CheckoutCode.InvalidCallback;
        var data = callback.Data;
        if (data.Provider != LocalSandboxGateway.ProviderName || data.OrderId == Guid.Empty || data.Currency != "VND" ||
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
            await LockCommerce(ct);
            var courseId = await db.Orders.Where(o => o.Id == notification.OrderId).Select(o => (Guid?)o.CourseId).SingleOrDefaultAsync(ct);
            if (!courseId.HasValue) return PaymentConfirmation.NotFound;
            var course = await LockCourse(courseId.Value, ct);
            var order = await db.Orders.SingleAsync(o => o.Id == notification.OrderId, ct);
            if (order.Provider != gateway.Provider || order.Amount != notification.Amount || order.Currency != notification.Currency ||
                !order.ExpiresAtUtc.HasValue || notification.OccurredAtUtc < order.CreatedAtUtc.AddMinutes(-1) ||
                notification.OccurredAtUtc > order.ExpiresAtUtc || notification.OccurredAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
                return PaymentConfirmation.Invalid;
            var existing = await db.Payments.FromSqlInterpolated($"SELECT * FROM Payment WITH (UPDLOCK, HOLDLOCK) WHERE ProviderTransactionId = {notification.ProviderTransactionId}")
                .SingleOrDefaultAsync(ct);
            var eligible = notification.Status == PaymentStatus.Succeeded && course is not null &&
                course.Status == CourseStatus.Published && course.IsPaid && CourseRules.ValidPrice(true, course.Price) &&
                await ActiveStudent(order.StudentUserId) &&
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
