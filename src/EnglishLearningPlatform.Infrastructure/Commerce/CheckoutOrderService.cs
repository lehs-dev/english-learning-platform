using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Commerce;

// Owns purchase eligibility, order creation and the student's order views.
public sealed class CheckoutOrderService(AppDbContext db, IActiveUserValidator activeUsers,
    IPaymentGateway hosted, ISandboxPaymentGateway local, CheckoutTransactionExecutor transactions) : ICheckoutOrderService
{
    private IPaymentGateway PurchaseGateway => hosted.IsConfigured ? hosted : local.IsConfigured ? local : hosted;
    private Task<bool> Enrolled(Guid user, Guid course, CancellationToken ct) => db.Enrollments.Valid()
        .AnyAsync(e => e.StudentUserId == user && e.CourseId == course, ct);
    private static PaymentStatus? ResolveStatus(IEnumerable<Payment> payments) =>
        payments.Any(p => p.Status == PaymentStatus.Succeeded) ? PaymentStatus.Succeeded :
        payments.Any(p => p.Status == PaymentStatus.Failed) ? PaymentStatus.Failed :
        payments.Any(p => p.Status == PaymentStatus.Cancelled) ? PaymentStatus.Cancelled : null;
    private async Task<CheckoutCode> Eligibility(Guid user, Course? course, CancellationToken ct)
    {
        if (!await activeUsers.IsActiveStudentAsync(user, ct)) return CheckoutCode.Forbidden;
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
            return new(CheckoutCode.Allowed, new(courseId, course!.Title, teacher, course.Price, "VND", local.IsConfigured, hosted.IsConfigured));
        }, new(CheckoutCode.IntegrationError), ct);

    private Task<CheckoutOutcome<OrderCheckout>> StartCore(Guid userId, Guid courseId, IPaymentGateway gateway, CancellationToken ct) =>
        transactions.ExecuteAsync<CheckoutOutcome<OrderCheckout>>("create-order", courseId, async () =>
        {
            await db.AcquireCommerceLockAsync(ct);
            var course = await db.CoursesForUpdate(courseId).SingleOrDefaultAsync(ct);
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
                null, false, local.IsConfigured, order.Provider, order.ExpiresAtUtc, order.CreatedAtUtc, url));
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
            if (!await activeUsers.IsActiveStudentAsync(userId, ct)) return new(CheckoutCode.Forbidden);
            var order = await db.Orders.AsNoTracking().Include(o => o.Course).Include(o => o.Payments)
                .SingleOrDefaultAsync(o => o.Id == orderId && o.StudentUserId == userId, ct);
            if (order is null) return new(CheckoutCode.NotFound);
            var enrolled = await Enrolled(userId, order.CourseId, ct);
            var status = ResolveStatus(order.Payments);
            var now = DateTimeOffset.UtcNow;
            var isLocal = order.Provider == local.Provider;
            var gateway = isLocal ? (IPaymentGateway)local : hosted;
            var isExpired = !status.HasValue && order.ExpiresAtUtc <= now;
            var canCheckout = !enrolled && !status.HasValue && order.ExpiresAtUtc > now &&
                order.Course.Status == CourseStatus.Published && order.Course.IsPaid && order.Amount == order.Course.Price &&
                gateway.IsConfigured && order.Provider == gateway.Provider &&
                await db.Users.AnyAsync(u => u.Id == order.Course.OwnerTeacherUserId && u.AccountStatus != AccountStatus.Disabled, ct);
            var teacher = await db.Users.Where(u => u.Id == order.Course.OwnerTeacherUserId).Select(u => u.FullName).SingleAsync(ct);
            return new(CheckoutCode.Allowed, new(order.Id, order.CourseId, order.Course.Title, teacher, order.Amount,
                order.Currency, status, enrolled && order.Course.Status != CourseStatus.Draft, local.IsConfigured,
                order.Provider, order.ExpiresAtUtc, order.CreatedAtUtc,
                canCheckout ? gateway.CreateCheckoutUrl(new(order.Id, order.Amount, order.Currency, order.ExpiresAtUtc!.Value)) : null, canCheckout)
            {
                IsExpired = isExpired
            });
        }, new(CheckoutCode.IntegrationError), ct);
    public Task<CheckoutOutcome<TransactionHistoryPage>> HistoryAsync(Guid userId, int page = 1, CancellationToken ct = default) =>
        transactions.ReadAsync<CheckoutOutcome<TransactionHistoryPage>>("transaction-history", userId, async () =>
        {
            if (!await activeUsers.IsActiveStudentAsync(userId, ct)) return new(CheckoutCode.Forbidden);

            var query = db.Orders.AsNoTracking().Where(o => o.StudentUserId == userId);
            var total = await query.CountAsync(ct);
            var pages = Math.Max(1, (total + TransactionHistoryPage.PageSize - 1) / TransactionHistoryPage.PageSize);
            page = Math.Clamp(page, 1, pages);
            var orders = await query.Include(o => o.Course).Include(o => o.Payments)
                .OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.Id)
                .Skip((page - 1) * TransactionHistoryPage.PageSize)
                .Take(TransactionHistoryPage.PageSize).ToListAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var items = orders.Select(o =>
            {
                var status = ResolveStatus(o.Payments);
                return new TransactionHistoryItem(o.Id, o.Course.Title,
                    o.Amount, o.Currency, o.CreatedAtUtc, status)
                {
                    ExpiresAtUtc = o.ExpiresAtUtc,
                    IsExpired = !status.HasValue && o.ExpiresAtUtc.HasValue && o.ExpiresAtUtc.Value <= now
                };
            }).ToList();
            return new(CheckoutCode.Allowed, new TransactionHistoryPage(items, page, total));
        }, new(CheckoutCode.IntegrationError), ct);

    public async Task<LearningOutcome<CheckoutPage>> GetAsync(Guid userId, Guid orderId, CancellationToken ct = default)
    {
        var result = await GetOrderAsync(userId, orderId, ct);
        if (result.Code != CheckoutCode.Allowed) return new(result.Code == CheckoutCode.NotFound ? LearningAccessResult.NotFound : LearningAccessResult.Forbidden);
        var order = result.Value!;
        return new(LearningAccessResult.Allowed, new(order.Id, order.CourseId, order.Title, order.Amount, order.Currency,
            order.Status ?? PaymentStatus.Pending, order.CanLearn, order.ExpiresAtUtc, order.CheckoutUrl, order.IsExpired));
    }
}
