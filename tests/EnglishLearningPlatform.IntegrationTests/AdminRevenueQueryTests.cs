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

public sealed class AdminRevenueQueryTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task Revenue_UsesFirstValidReceiptPerOrder_ExcludesNonVnd_AndKeepsCreationCohortDistinct()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IAdminRevenueQueryService>();
        // This fixture has no business data. No invented currency row or revenue is returned.
        var empty = await service.GetAsync(admin.Id);
        Assert.NotNull(empty);
        Assert.Empty(empty.Summary);
        Assert.Empty(empty.Daily);
        Assert.Empty(empty.Monthly);

        var midnight = new DateTimeOffset(2026, 10, 22, 17, 0, 0, TimeSpan.Zero);
        var nextMidnight = midnight.AddDays(1);
        var course = new Course { Title = "Revenue receipt reconciliation", OwnerTeacherUserId = teacher.Id };
        Order AddOrder(decimal amount, string currency = "VND", DateTimeOffset? created = null)
        {
            var order = new Order { Course = course, StudentUserId = student.Id, Amount = amount,
                Currency = currency, CreatedAtUtc = created ?? midnight.AddHours(1) };
            db.Orders.Add(order);
            return order;
        }
        void AddPayment(Order order, PaymentStatus status, DateTimeOffset? paid = null, decimal? amount = null)
            => db.Payments.Add(new Payment { Order = order, Amount = amount ?? order.Amount, Status = status,
                PaidAtUtc = paid, ProviderTransactionId = $"revenue-{Guid.NewGuid():N}" });

        var retry = AddOrder(100);
        AddPayment(retry, PaymentStatus.Failed);
        AddPayment(retry, PaymentStatus.Succeeded, midnight.AddHours(-1), 99);
        AddPayment(retry, PaymentStatus.Succeeded); // Malformed retries must not beat a valid receipt.
        AddPayment(retry, PaymentStatus.Succeeded, midnight);
        AddPayment(retry, PaymentStatus.Succeeded, nextMidnight.AddHours(1));
        AddPayment(retry, PaymentStatus.Failed); // A later failed attempt cannot undo the receipt.
        AddPayment(AddOrder(200), PaymentStatus.Succeeded, nextMidnight.AddTicks(-1));
        AddPayment(AddOrder(10, "usd"), PaymentStatus.Succeeded, midnight.AddHours(3));
        var failed = AddOrder(50);
        AddPayment(failed, PaymentStatus.Failed);
        AddPayment(failed, PaymentStatus.Cancelled);
        AddPayment(AddOrder(30), PaymentStatus.Cancelled);
        AddOrder(20); // No Payment is still a pending Order.
        var wrongAmount = AddOrder(80);
        AddPayment(wrongAmount, PaymentStatus.Succeeded, midnight.AddHours(4), 79);
        AddPayment(wrongAmount, PaymentStatus.Failed);
        AddPayment(AddOrder(90), PaymentStatus.Succeeded); // Missing PaidAt: not booked as cash.
        AddPayment(AddOrder(0), PaymentStatus.Succeeded, midnight); // Zero-value checkout is not revenue.
        AddPayment(AddOrder(120, created: midnight.AddDays(-1)), PaymentStatus.Succeeded, midnight.AddHours(5));
        AddPayment(AddOrder(140), PaymentStatus.Succeeded, midnight.AddTicks(-1));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var all = (await service.GetAsync(admin.Id))!;
        var vnd = Assert.Single(all.Summary, row => row.Currency == "VND");
        Assert.Equal(560m, vnd.Revenue);
        Assert.Equal(4, vnd.PaidOrders);
        Assert.Equal(10, vnd.Orders);
        Assert.Equal(7, vnd.SucceededOrders);
        Assert.Equal(1, vnd.PendingOrders);
        Assert.Equal(1, vnd.FailedOrders);
        Assert.Equal(1, vnd.CancelledOrders);
        Assert.Equal(3, vnd.UnrecognizedSucceededOrders);
        Assert.Single(all.Summary);
        Assert.All(all.Summary, row => Assert.Equal("VND", row.Currency));
        Assert.All(all.Daily, row => Assert.Equal("VND", row.Currency));
        Assert.All(all.Monthly, row => Assert.Equal("VND", row.Currency));
        Assert.Equal(560m, Assert.Single(all.Monthly, row => row.Currency == "VND").Revenue);
        Assert.Equal(new DateOnly(2026, 10, 1), all.Monthly[0].Period);

        var day = new AdminDateRange(new(2026, 10, 23), new(2026, 10, 23));
        var filtered = (await service.GetAsync(admin.Id, day, "VND"))!;
        Assert.Equal(day, filtered.Dates);
        Assert.Equal("VND", filtered.CurrencyFilter);
        var summary = Assert.Single(filtered.Summary);
        Assert.Equal(420m, summary.Revenue);
        Assert.Equal(3, summary.PaidOrders);
        Assert.Equal(9, summary.Orders);
        Assert.Equal(6, summary.SucceededOrders);
        Assert.Equal(1, summary.PendingOrders);
        Assert.Equal(1, summary.FailedOrders);
        Assert.Equal(1, summary.CancelledOrders);
        Assert.Equal(3, summary.UnrecognizedSucceededOrders);
        var daily = Assert.Single(filtered.Daily);
        Assert.Equal(new DateOnly(2026, 10, 23), daily.Period);
        Assert.Equal(420m, daily.Revenue);
        Assert.Equal(3, daily.PaidOrders);
        Assert.Equal(420m, Assert.Single(filtered.Monthly).Revenue);
        var normalized = (await service.GetAsync(admin.Id, day, " vnd "))!;
        Assert.Equal("VND", normalized.CurrencyFilter);
        Assert.Equal(summary, Assert.Single(normalized.Summary));
        // Choose the first valid receipt BEFORE filtering. The duplicate next-day payment cannot earn twice.
        var later = (await service.GetAsync(admin.Id, new(new(2026, 10, 24), null)))!;
        Assert.Empty(later.Daily);
        Assert.Empty(later.Monthly);
        Assert.Empty(later.Summary);
        Assert.Empty(db.ChangeTracker.Entries());

        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        foreach (var groupBy in new[] { "day", "month" })
        {
            using var response = await client.GetAsync($"/Admin/Revenue?fromDate=2026-10-23&toDate=2026-10-23&currency=VND&groupBy={groupBy}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("value=\"2026-10-23\"", html);
            Assert.DoesNotContain("id=\"currency\"", html);
            Assert.DoesNotContain("USD", html);
            var evidenceDirectory = Environment.GetEnvironmentVariable("ELP_REVENUE_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(evidenceDirectory))
            {
                Directory.CreateDirectory(evidenceDirectory);
                await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, $"sample-revenue-{groupBy}.html"), html);
            }
        }
        foreach (var invalid in new[] { "fromDate=2026-10-24&toDate=2026-10-23", "fromDate=bad",
                     "toDate=9999-12-31", "fromDate=0001-01-01" })
        {
            using var response = await client.GetAsync($"/Admin/Revenue?{invalid}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("validation-summary-errors", html);
            Assert.DoesNotContain("data-revenue-currency=", html);
        }

        // More than one page of aggregate periods: navigation must retain the complete report filter.
        db.Courses.Attach(course);
        for (var dayOfMonth = 1; dayOfMonth <= 21; dayOfMonth++)
        {
            var receipt = new DateTimeOffset(2026, 10, dayOfMonth, 12, 0, 0, TimeSpan.FromHours(7));
            AddPayment(AddOrder(1, "VND", receipt), PaymentStatus.Succeeded, receipt);
        }
        await db.SaveChangesAsync();
        var firstPage = await client.GetStringAsync("/Admin/Revenue?fromDate=2026-10-01&toDate=2026-10-31&currency=VND&groupBy=day");
        var nextLink = System.Text.RegularExpressions.Regex.Matches(firstPage, "href=\"([^\"]+)\"")
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
            .Single(href => href.Contains("page=2", StringComparison.Ordinal));
        foreach (var filter in new[] { "fromDate=2026-10-01", "toDate=2026-10-31", "currency=VND", "groupBy=day" })
            Assert.Contains(filter, nextLink);
        using var secondPage = await client.GetAsync(nextLink);
        Assert.Equal(HttpStatusCode.OK, secondPage.StatusCode);
        Assert.Contains("01/10/2026", await secondPage.Content.ReadAsStringAsync());
    }
}

// Independent database: empty/month-boundary assertions do not depend on another test's execution order.
public sealed class AdminRevenueBoundaryTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData("week",2026,10,25,"2026-10-19","2026-10-25")]
    [InlineData("week",2027,1,1,"2026-12-28","2027-01-03")]
    [InlineData("month",2024,2,17,"2024-02-01","2024-02-29")]
    [InlineData("year",2026,10,24,"2026-01-01","2026-12-31")]
    public void CalendarPresets_UseMondayWeeks_AndFullCalendarMonthsAndYears(string period,int year,int month,int day,string start,string end)
    {
        var range=AdminRevenueRanges.Resolve(period,new(year,month,day),null,null);
        Assert.Equal(DateOnly.Parse(start),range.FromDate);
        Assert.Equal(DateOnly.Parse(end),range.ToDate);
    }

    [Fact]
    public async Task RevenuePresets_ResolveVietnamDates_AndChartKeepsZeroRevenueCalendarGaps()
    {
        var admin=await IdentityTestHelpers.CreateUserAsync(factory,AppRoles.Admin);
        using var client=IdentityTestHelpers.CreateClient(factory);
        using var login=await IdentityTestHelpers.LoginAsync(client,admin);
        var today=DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var defaultHtml=await client.GetStringAsync("/Admin/Revenue");
        var currentMonth=AdminRevenueRanges.Resolve("month",today,null,null);
        Assert.Contains("value=\"month\" selected=\"selected\"",defaultHtml);
        Assert.Contains($"value=\"{currentMonth.FromDate:yyyy-MM-dd}\"",defaultHtml);
        Assert.Contains($"value=\"{currentMonth.ToDate:yyyy-MM-dd}\"",defaultHtml);
        var allHtml=await client.GetStringAsync("/Admin/Revenue?period=all");
        Assert.Contains("value=\"all\" selected=\"selected\"",allHtml);
        var allFromInput=System.Text.RegularExpressions.Regex.Match(allHtml,"<input[^>]*id=\"fromDate\"[^>]*>").Value;
        Assert.NotEmpty(allFromInput);
        Assert.DoesNotMatch("value=\"[^\"]+\"",allFromInput);
        foreach(var period in new[]{"week","month","year"})
        {
            var expected=AdminRevenueRanges.Resolve(period,today,null,null);
            var html=await client.GetStringAsync($"/Admin/Revenue?period={period}&groupBy=month");
            Assert.Contains($"value=\"{expected.FromDate:yyyy-MM-dd}\"",html);
            Assert.Contains($"value=\"{expected.ToDate:yyyy-MM-dd}\"",html);
            Assert.DoesNotContain(">Về tổng quan</a>",html);
        }
        using var bad=await client.GetAsync("/Admin/Revenue?period=unknown");
        Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        var report=new AdminRevenueReport(new(new(2026,10,1),new(2026,10,3)),"VND",[],
            [new(new(2026,10,1),"VND",100,1),new(new(2026,10,3),"VND",200,1)],[]);
        var model=new EnglishLearningPlatform.Web.Models.Admin.AdminRevenueViewModel(report,"day",1);
        Assert.Equal(new[]{100m,0m,200m},model.ChartPoints.Select(p=>p.Revenue));
        Assert.Contains("01/10/2026",model.RangeLabel);
        Assert.Contains("03/10/2026",model.RangeLabel);
    }

    [Fact]
    public async Task Periods_UseVietnamDayAndMonth_RespectInclusiveEnd_AndNormalizeOffsets()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IAdminRevenueQueryService>();
        var monthStart = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(7));
        var monthEnd = monthStart.AddMonths(1);
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Month boundary" };
        foreach (var (amount, paid) in new[] { (10m, monthStart.AddTicks(-1)), (20m, monthStart),
                     (30m, monthEnd.AddTicks(-1)), (40m, monthEnd.ToUniversalTime()) })
        {
            var order = new Order { Course = course, StudentUserId = admin.Id, Amount = amount, CreatedAtUtc = paid };
            db.Payments.Add(new Payment { Order = order, Status = PaymentStatus.Succeeded, Amount = amount,
                PaidAtUtc = paid, ProviderTransactionId = $"boundary-{Guid.NewGuid():N}" });
        }
        await db.SaveChangesAsync();
        var october = (await service.GetAsync(admin.Id, new(new(2026, 10, 1), new(2026, 10, 31))))!;
        var summary = Assert.Single(october.Summary);
        Assert.Equal(50m, summary.Revenue);
        Assert.Equal(2, summary.PaidOrders);
        Assert.Equal(2, summary.Orders);
        Assert.Equal(2, summary.SucceededOrders);
        Assert.Equal(new[] { new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31) }, october.Daily.Select(r => r.Period));
        Assert.Equal(new[] { 20m, 30m }, october.Daily.Select(r => r.Revenue));
        Assert.Equal(new DateOnly(2026, 10, 1), Assert.Single(october.Monthly).Period);
        Assert.Equal(50m, october.Monthly[0].Revenue);
        var all = (await service.GetAsync(admin.Id))!;
        Assert.Equal(new[] { new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1) }, all.Monthly.Select(r => r.Period));
        Assert.Equal(new[] { 10m, 50m, 40m }, all.Monthly.Select(r => r.Revenue));
        var throughOctober = (await service.GetAsync(admin.Id, new(null, new(2026, 10, 31))))!;
        Assert.Equal(60m, Assert.Single(throughOctober.Summary).Revenue);
        var fromNovember = (await service.GetAsync(admin.Id, new(new(2026, 11, 1), null)))!;
        Assert.Equal(40m, Assert.Single(fromNovember.Summary).Revenue);
    }
}

public sealed class AdminRevenueAuthorizationTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Teacher)]
    public async Task NonAdmin_CannotReadRevenueThroughServiceOrMvc(string role)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminRevenueQueryService>();
        Assert.Null(await service.GetAsync(user.Id));
        Assert.Null(await service.GetAsync(Guid.NewGuid()));
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        using var response = await client.GetAsync("/Admin/Revenue");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData(AccountStatus.Locked)]
    [InlineData(AccountStatus.Disabled)]
    public async Task InactiveAdmin_CannotReadRevenue(AccountStatus status)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.SingleAsync(u => u.Id == user.Id)).AccountStatus = status;
        await db.SaveChangesAsync();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IAdminRevenueQueryService>().GetAsync(user.Id));
    }

    [Fact]
    public async Task Service_RejectsInvalidRangesAndCurrency_AndMvcRejectsInvalidNonDateFilters()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminRevenueQueryService>();
        foreach (var range in new[] { new AdminDateRange(new(2026, 10, 24), new(2026, 10, 23)),
                     new AdminDateRange(DateOnly.MinValue, null), new AdminDateRange(null, DateOnly.MaxValue) })
            await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(admin.Id, range));
        foreach (var currency in new[] { "USD", "JPY", "VN", "VNDD", "V1D", "<script>" })
            await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(admin.Id, currency: currency));
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        foreach (var query in new[] { "currency=USD", "currency=JPY", "currency=VN", "currency=V1D", "groupBy=year", "groupBy=bad", "page=bad" })
        {
            using var response = await client.GetAsync($"/Admin/Revenue?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var empty = await client.GetAsync("/Admin/Revenue");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.True(empty.Headers.CacheControl!.NoStore);
        Assert.DoesNotContain("data-revenue-currency=", await empty.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Guest_MustLoginToReadRevenue()
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.GetAsync("/Admin/Revenue");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }
}
