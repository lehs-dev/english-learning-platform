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

public sealed class AdminDateFilterTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Succeeded)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Cancelled)]
    public async Task Transactions_CombineDateSearchAndLatestStatus_AcrossDisjointStablePages(PaymentStatus status)
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IAdminQueryService>();
        var marker = $"combined-{Guid.NewGuid():N}";
        var start = new DateTimeOffset(2026, 12, 30, 17, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(1);
        var course = new Course { Title = marker, OwnerTeacherUserId = teacher.Id };
        Order AddOrder(Course target, DateTimeOffset created, PaymentStatus? latest)
        {
            var order = new Order { Course = target, StudentUserId = student.Id,
                Amount = 199000m, Currency = "VND", CreatedAtUtc = created };
            db.Orders.Add(order);
            if (latest.HasValue)
            {
                // Transactions intentionally displays the latest attempt, unlike booked revenue.
                db.Payments.Add(new Payment { Order = order, Amount = order.Amount,
                    ProviderTransactionId = $"combined-{Guid.NewGuid():N}",
                    Status = latest == PaymentStatus.Failed ? PaymentStatus.Pending : PaymentStatus.Failed,
                    CreatedAtUtc = created.AddMinutes(1) });
                db.Payments.Add(new Payment { Order = order, Amount = order.Amount, Status = latest.Value,
                    ProviderTransactionId = $"combined-{Guid.NewGuid():N}",
                    CreatedAtUtc = created.AddMinutes(2),
                    PaidAtUtc = latest == PaymentStatus.Succeeded ? created.AddMinutes(2) : null });
            }
            return order;
        }

        // Equal timestamps exercise the OrderId tie-breaker; pending includes absent payments.
        var matching = Enumerable.Range(0, 21)
            .Select(i => AddOrder(course, start, status == PaymentStatus.Pending && i % 2 == 0 ? null : status))
            .ToList();
        matching.Add(AddOrder(course, end.AddTicks(-1), status));
        AddOrder(course, start.AddTicks(-1), status);
        AddOrder(course, end, status);
        AddOrder(course, start, status == PaymentStatus.Failed ? PaymentStatus.Succeeded : PaymentStatus.Failed);
        AddOrder(new Course { Title = $"other-{Guid.NewGuid():N}", OwnerTeacherUserId = teacher.Id }, start, status);
        await db.SaveChangesAsync();
        var orderCount = await db.Orders.CountAsync();
        var paymentCount = await db.Payments.CountAsync();
        db.ChangeTracker.Clear();
        var dates = new AdminDateRange(new(2026, 12, 31), new(2026, 12, 31));
        var first = (await service.TransactionsAsync(admin.Id, marker, status, 1, dates: dates))!;
        var second = (await service.TransactionsAsync(admin.Id, marker, status, 2, dates: dates))!;
        Assert.Equal(22, first.Total);
        Assert.Equal(2, first.Pages);
        Assert.Equal(20, first.Items.Count);
        Assert.Equal(2, second.Items.Count);
        Assert.Empty(first.Items.Select(x => x.OrderId).Intersect(second.Items.Select(x => x.OrderId)));
        Assert.Equal(matching.Select(x => x.Id).Order(),
            first.Items.Concat(second.Items).Select(x => x.OrderId).Order());
        Assert.All(first.Items.Concat(second.Items), row =>
        {
            Assert.Equal(status, row.Status ?? PaymentStatus.Pending);
            Assert.InRange(row.CreatedAtUtc, start, end.AddTicks(-1));
        });
        Assert.Equal(first.Items, (await service.TransactionsAsync(admin.Id, marker, status, 1, dates: dates))!.Items);
        Assert.Equal(2, (await service.TransactionsAsync(admin.Id, marker, status, 999, dates: dates))!.Page);
        Assert.Empty(db.ChangeTracker.Entries());

        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        var html = await client.GetStringAsync($"/Admin/Transactions?search={marker}&status={status}&fromDate=2026-12-31&toDate=2026-12-31");
        var next = System.Text.RegularExpressions.Regex.Matches(html, "href=\"([^\"]+)\"")
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .Single(href => href.Contains("page=2", StringComparison.Ordinal));
        foreach (var filter in new[] { $"search={marker}", $"status={status}", "fromDate=2026-12-31", "toDate=2026-12-31" })
            Assert.Contains(filter, next);
        using var response = await client.GetAsync(next);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pageTwo = await response.Content.ReadAsStringAsync();
        foreach (var row in second.Items) Assert.Contains(row.OrderId.ToString(), pageTwo);
        foreach (var row in first.Items) Assert.DoesNotContain(row.OrderId.ToString(), pageTwo);
        Assert.Equal(orderCount, await db.Orders.CountAsync());
        Assert.Equal(paymentCount, await db.Payments.CountAsync());
    }

    [Fact]
    public async Task AllLists_FilterVietnamCalendarDay_BeforeCountingAndPaging()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var query = scope.ServiceProvider.GetRequiredService<IAdminQueryService>();
        var marker = $"date-{Guid.NewGuid():N}";
        var start = new DateTimeOffset(2026, 10, 22, 17, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(1);
        var times = new[] { start.AddTicks(-1), start, end.AddTicks(-1), end };
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = marker };
        foreach (var time in times)
        {
            db.Orders.Add(new Order { Course = course, StudentUserId = student.Id, CreatedAtUtc = time });
            db.AuditLogs.Add(new AuditLog { ActorSnapshot = marker, Action = "UpdateUser", OccurredAtUtc = time });
        }
        var account = await db.Users.SingleAsync(u => u.Id == student.Id);
        account.CreatedAtUtc = end.AddTicks(-1);
        await db.SaveChangesAsync();
        var dates = new AdminDateRange(new(2026, 10, 23), new(2026, 10, 23));
        var orders = await query.TransactionsAsync(admin.Id, marker, PaymentStatus.Pending, 99, dates: dates);
        var logs = await query.AuditAsync(admin.Id, marker, 99, dates: dates);
        var users = await query.UsersAsync(admin.Id, student.Email, AccountStatus.Active, 1, role: AppRoles.Student, dates: dates);
        Assert.Equal(2, orders!.Total);
        Assert.Equal(1, orders.Page);
        Assert.Equal(2, logs!.Total);
        Assert.Equal(1, users!.Total);
        Assert.Equal(3, (await query.TransactionsAsync(admin.Id, marker, null, 1, dates: new(null, new(2026, 10, 23))))!.Total);
        Assert.Equal(3, (await query.AuditAsync(admin.Id, marker, 1, dates: new(new(2026, 10, 23), null)))!.Total);
        Assert.Empty((await query.UsersAsync(admin.Id, student.Email, null, 1, dates: new(new(2026, 10, 24), null)))!.Items);
        Assert.Null(await query.AuditAsync(student.Id, marker, 1, dates: dates));
    }

    [Theory]
    [InlineData("Users")]
    [InlineData("Transactions")]
    [InlineData("Audit")]
    public async Task Pages_KeepDatesInPagination_AndExplainInvalidRanges(string action)
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var marker = $"dates-page-{Guid.NewGuid():N}";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = new Course { Title = marker, OwnerTeacherUserId = teacher.Id };
            var time = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
            for (var i = 0; i < 21; i++)
            {
                db.Orders.Add(new Order { Course = course, StudentUserId = admin.Id, CreatedAtUtc = time });
                db.AuditLogs.Add(new AuditLog { ActorSnapshot = marker, Action = "UpdateUser", OccurredAtUtc = time });
                db.Users.Add(new ApplicationUser { FullName = marker, UserName = $"{marker}-{i}", CreatedAtUtc = time });
            }
            await db.SaveChangesAsync();
        }
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        var html = await client.GetStringAsync($"/Admin/{action}?search={marker}&fromDate=2026-01-01&toDate=2026-12-31");
        Assert.Contains("type=\"date\"", html);
        Assert.Contains("value=\"2026-01-01\"", html);
        Assert.Contains("value=\"2026-12-31\"", html);
        Assert.Contains("fromDate=2026-01-01&amp;toDate=2026-12-31", html);
        Assert.Contains("page=2", html);
        foreach (var input in new[] { "fromDate=2026-10-24&toDate=2026-10-23", "fromDate=bad", "toDate=9999-12-31", "fromDate=0001-01-01" })
        {
            using var response = await client.GetAsync($"/Admin/{action}?{input}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
        }
    }
}
