using System.Net;
using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// A separate fixture keeps exact totals independent of all other Admin tests.
public sealed class AdminRevenueAcceptanceTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task YearBoundary_RetriesCannotMoveBookedRevenue_AndMonthlyTotalsReconcileWithDailyTotals()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IAdminRevenueQueryService>();
        var nextYear = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(7));
        var course = new Course { Title = "Year-end VND receipt reconciliation", OwnerTeacherUserId = teacher.Id };
        Order AddOrder(decimal amount, DateTimeOffset created)
        {
            var order = new Order { Course = course, StudentUserId = student.Id,
                Amount = amount, Currency = "VND", CreatedAtUtc = created };
            db.Orders.Add(order);
            return order;
        }
        void Receipt(Order order, PaymentStatus status, DateTimeOffset at)
            => db.Payments.Add(new Payment { Order = order, Amount = order.Amount, Status = status,
                CreatedAtUtc = at, PaidAtUtc = status == PaymentStatus.Succeeded ? at : null,
                ProviderTransactionId = $"acceptance-{Guid.NewGuid():N}" });

        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        using (var empty = await client.GetAsync("/Admin/Revenue?period=custom&fromDate=2026-12-31&toDate=2027-01-01"))
        {
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            Assert.DoesNotContain("data-revenue-currency=", await empty.Content.ReadAsStringAsync());
        }

        var december = AddOrder(199000m, nextYear.AddDays(-1));
        Receipt(december, PaymentStatus.Failed, nextYear.AddMinutes(-10));
        Receipt(december, PaymentStatus.Succeeded, nextYear.AddTicks(-1));
        Receipt(december, PaymentStatus.Succeeded, nextYear.ToUniversalTime());
        Receipt(december, PaymentStatus.Failed, nextYear.AddMinutes(1));
        var january = AddOrder(299000m, nextYear.AddDays(-1));
        Receipt(january, PaymentStatus.Succeeded, nextYear.ToUniversalTime());
        Receipt(january, PaymentStatus.Succeeded, nextYear.AddDays(1));
        Receipt(AddOrder(99000m, nextYear), PaymentStatus.Failed, nextYear);
        Receipt(AddOrder(149000m, nextYear), PaymentStatus.Cancelled, nextYear);
        AddOrder(249000m, nextYear); // Pending, without a receipt.
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var lastDay = (await service.GetAsync(admin.Id, new(new(2026, 12, 31), new(2026, 12, 31))))!;
        var firstDay = (await service.GetAsync(admin.Id, new(new(2027, 1, 1), new(2027, 1, 1))))!;
        Assert.Equal(199000m, Assert.Single(lastDay.Summary).Revenue);
        Assert.Equal(299000m, Assert.Single(firstDay.Summary).Revenue);
        Assert.Equal(1, lastDay.Summary[0].PaidOrders);
        Assert.Equal(1, firstDay.Summary[0].PaidOrders);
        // Both successful Orders were created in December, but January received one payment.
        Assert.Equal(2, lastDay.Summary[0].SucceededOrders);
        Assert.Equal(0, firstDay.Summary[0].SucceededOrders);
        Assert.Equal(3, firstDay.Summary[0].Orders);
        Assert.Equal(1, firstDay.Summary[0].PendingOrders);
        Assert.Equal(1, firstDay.Summary[0].FailedOrders);
        Assert.Equal(1, firstDay.Summary[0].CancelledOrders);

        var all = (await service.GetAsync(admin.Id))!;
        var total = Assert.Single(all.Summary);
        Assert.Equal(498000m, total.Revenue);
        Assert.Equal(2, total.PaidOrders);
        Assert.Equal(5, total.Orders);
        Assert.Equal(total.Revenue, all.Daily.Sum(row => row.Revenue));
        Assert.Equal(total.Revenue, all.Monthly.Sum(row => row.Revenue));
        Assert.Equal(total.PaidOrders, all.Daily.Sum(row => row.PaidOrders));
        Assert.Equal(total.PaidOrders, all.Monthly.Sum(row => row.PaidOrders));
        Assert.Equal(new[] { new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 1) }, all.Monthly.Select(row => row.Period));
        Assert.Equal(new[] { 199000m, 299000m }, all.Monthly.Select(row => row.Revenue));
        var retryDay = (await service.GetAsync(admin.Id, new(new(2027, 1, 2), new(2027, 1, 2))))!;
        Assert.Empty(retryDay.Daily);
        Assert.Empty(retryDay.Monthly);
        Assert.Empty(retryDay.Summary);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(5, await db.Orders.CountAsync());
        Assert.Equal(8, await db.Payments.CountAsync());
    }
}
