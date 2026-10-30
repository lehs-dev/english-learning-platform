using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Commerce;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class VnpayCheckoutTests(CheckoutTestFactory factory) : IClassFixture<CheckoutTestFactory>
{
    private static readonly string TestKey = new('v', 32); // Synthetic test-only HMAC key.

    [Fact]
    public async Task Vnpay_IpnIsAuthoritative_AtomicPrivateIdempotent_AndReconcilesWithAdminRevenue()
    {
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vnpay:UseIpn"] = "true", ["Vnpay:Enabled"] = "true", ["Vnpay:TmnCode"] = "TEST0001", ["Vnpay:HashSecret"] = TestKey,
                ["Vnpay:PublicBaseUrl"] = "https://merchant.example.test", ["Payments:Enabled"] = "false"
            })));
        var teacher = await IdentityTestHelpers.CreateUserAsync(app, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(app, AppRoles.Student);
        var other = await IdentityTestHelpers.CreateUserAsync(app, AppRoles.Student);
        var admin = await IdentityTestHelpers.CreateUserAsync(app, AppRoles.Admin);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orders = scope.ServiceProvider.GetRequiredService<ICheckoutOrderService>();
        Course Course(decimal price)
        {
            var course = new Course { Title = $"VNPay {Guid.NewGuid():N}", OwnerTeacherUserId = teacher.Id,
                IsPaid = true, Price = price, Status = CourseStatus.Published };
            db.Courses.Add(course); return course;
        }
        var course = Course(199000m);
        await db.SaveChangesAsync();
        using var client = IdentityTestHelpers.CreateClient(app);
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        var preview = await client.GetStringAsync($"/Checkout?courseId={course.Id}");
        Assert.Contains("VNPay Sandbox", preview);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Checkout?courseId={course.Id}");
        using var create = await client.PostAsync("/Checkout/CreateOrder", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["CourseId"] = course.Id.ToString(), ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var order = (await orders.CreateOrderAsync(student.Id, course.Id)).Value!;
        Assert.Equal(VnpayGateway.ProviderName, order.Provider);
        Assert.StartsWith(VnpayGateway.PaymentUrl, order.CheckoutUrl!);
        Assert.Single(await db.Orders.Where(o => o.CourseId == course.Id).ToListAsync());
        Assert.False(await db.Payments.AnyAsync());
        Assert.False(await db.Enrollments.AnyAsync());
        var pending = await client.GetStringAsync(create.Headers.Location);
        Assert.Contains("VNPay Sandbox", pending);
        Assert.DoesNotContain("name=\"scenario\"", pending);
        using var forgedLocal = await client.PostAsync($"/Checkout/Simulate/{order.Id}", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["scenario"] = "success", ["__RequestVerificationToken"] = token }));
        Assert.False(await db.Payments.AnyAsync());

        var callback = Notification(order.Id, order.Amount, "123456789");
        var returnUrl = QueryHelpers.AddQueryString("/payments/vnpay/return", callback!);
        using var returned = await client.GetAsync(returnUrl);
        Assert.Equal(HttpStatusCode.Redirect, returned.StatusCode);
        Assert.Contains(order.Id.ToString(), returned.Headers.Location!.ToString());
        Assert.False(await db.Payments.AnyAsync()); // Browser return must not settle payment.
        using var anonymous = IdentityTestHelpers.CreateClient(app);
        using var guestReturn = await anonymous.GetAsync(returnUrl);
        Assert.Contains(order.Id.ToString(), guestReturn.Headers.Location!.ToString());
        using var guestResult = await anonymous.GetAsync(guestReturn.Headers.Location);
        Assert.Contains("/Account/Login", guestResult.Headers.Location!.ToString());
        Assert.DoesNotContain("vnp_SecureHash", guestResult.Headers.Location.ToString());
        using var otherClient = IdentityTestHelpers.CreateClient(app);
        using var otherLogin = await IdentityTestHelpers.LoginAsync(otherClient, other);
        using var otherReturn = await otherClient.GetAsync(returnUrl);
        using var privatePage = await otherClient.GetAsync(otherReturn.Headers.Location);
        Assert.Equal(HttpStatusCode.NotFound, privatePage.StatusCode);

        var tampered = new Dictionary<string, string>(callback) { ["vnp_Amount"] = "100" };
        Assert.Equal("97", await Ipn(anonymous, tampered));
        var wrongMerchant = new Dictionary<string, string>(callback) { ["vnp_TmnCode"] = "OTHER001" }; Sign(wrongMerchant);
        Assert.Equal("97", await Ipn(anonymous, wrongMerchant));
        var wrongAmount = Notification(order.Id, 1m, "123456789");
        Assert.Equal("04", await Ipn(anonymous, wrongAmount));
        Assert.Equal("01", await Ipn(anonymous, Notification(Guid.NewGuid(), 199000m, "123456789")));
        using var duplicateQuery = await anonymous.GetAsync(QueryHelpers.AddQueryString("/payments/vnpay/ipn", callback!) + "&vnp_Amount=1");
        Assert.Contains("\"RspCode\":\"97\"", await duplicateQuery.Content.ReadAsStringAsync());
        Assert.False(await db.Enrollments.AnyAsync());

        Assert.Equal("00", await Ipn(anonymous, callback));
        Assert.Equal("02", await Ipn(anonymous, callback));
        var replayResults = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Ipn(anonymous, callback)));
        Assert.All(replayResults, code => Assert.Equal("02", code));
        var payment = await db.Payments.SingleAsync(p => p.OrderId == order.Id);
        Assert.Equal(199000m, payment.Amount);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.NotNull(payment.PaidAtUtc);
        var enrollment = await db.Enrollments.SingleAsync(e => e.CourseId == course.Id);
        Assert.Equal(payment.Id, enrollment.PaymentId);
        Assert.True((await orders.GetOrderAsync(student.Id, order.Id)).Value!.CanLearn);
        Assert.Equal("99", await Ipn(anonymous, Notification(order.Id, order.Amount, "123456790")));
        var history = (await orders.HistoryAsync(student.Id)).Value!;
        Assert.Equal(PaymentStatus.Succeeded, Assert.Single(history.Orders).Status);

        // Error after Payment INSERT must roll back Payment, Enrollment and receipt audit together.
        var rollbackCourse = Course(299000m); await db.SaveChangesAsync();
        var rollbackOrder = (await orders.CreateOrderAsync(student.Id, rollbackCourse.Id)).Value!;
        var rollbackCallback = Notification(rollbackOrder.Id, rollbackOrder.Amount, "223456789");
        factory.Faults.FailEnrollment = true;
        try { Assert.Equal("99", await Ipn(anonymous, rollbackCallback)); }
        finally { factory.Faults.FailEnrollment = false; }
        Assert.True(factory.Faults.SawSavedPayment);
        Assert.False(await db.Payments.AnyAsync(p => p.OrderId == rollbackOrder.Id));
        Assert.False(await db.Enrollments.AnyAsync(e => e.CourseId == rollbackCourse.Id));
        Assert.Equal("00", await Ipn(anonymous, rollbackCallback));

        foreach (var (response, expected) in new[] { ("24", PaymentStatus.Cancelled), ("51", PaymentStatus.Failed) })
        {
            var unsuccessfulCourse = Course(49000m); await db.SaveChangesAsync();
            var unsuccessful = (await orders.CreateOrderAsync(student.Id, unsuccessfulCourse.Id)).Value!;
            var data = Notification(unsuccessful.Id, unsuccessful.Amount, "0", response, "02");
            data.Remove("vnp_PayDate"); Sign(data);
            Assert.Equal("00", await Ipn(anonymous, data));
            Assert.Equal("02", await Ipn(anonymous, data));
            var receipt = await db.Payments.SingleAsync(p => p.OrderId == unsuccessful.Id);
            Assert.Equal(expected, receipt.Status);
            Assert.Null(receipt.PaidAtUtc);
            Assert.False(await db.Enrollments.AnyAsync(e => e.CourseId == unsuccessfulCourse.Id));
            Assert.NotEqual(unsuccessful.Id, (await orders.CreateOrderAsync(student.Id, unsuccessfulCourse.Id)).Value!.Id);
        }

        // A signed success after a course withdrawal is still a receipt, but cannot grant access.
        var withdrawn = Course(99000m); await db.SaveChangesAsync();
        var withdrawnOrder = (await orders.CreateOrderAsync(student.Id, withdrawn.Id)).Value!;
        withdrawn.Status = CourseStatus.Unpublished; await db.SaveChangesAsync();
        Assert.Equal("00", await Ipn(anonymous, Notification(withdrawnOrder.Id, withdrawnOrder.Amount, "323456789")));
        Assert.False(await db.Enrollments.AnyAsync(e => e.CourseId == withdrawn.Id));
        db.ChangeTracker.Clear();
        var report = (await scope.ServiceProvider.GetRequiredService<IAdminRevenueQueryService>().GetAsync(admin.Id))!;
        var summary = Assert.Single(report.Summary);
        Assert.Equal(597000m, summary.Revenue);
        Assert.True(report.IncludesSandboxRevenue);
        Assert.Equal(3, summary.PaidOrders);
        Assert.Equal(597000m, report.Daily.Sum(p => p.Revenue));
        Assert.Equal(597000m, report.Monthly.Sum(p => p.Revenue));
        var dashboard = (await scope.ServiceProvider.GetRequiredService<IAdminDashboardQueryService>().GetAsync(admin.Id))!;
        Assert.Equal(2, dashboard.Enrollments);
        Assert.Equal(3, dashboard.SucceededOrders);
        var enrollmentDetails = (await scope.ServiceProvider.GetRequiredService<IAdminOverviewDetailQueryService>()
            .EnrollmentsAsync(admin.Id, course.Title, null, 1))!;
        var linked = Assert.Single(enrollmentDetails.Items);
        Assert.Equal(order.Id, linked.OrderId);
        Assert.Equal(payment.Id, linked.PaymentId);
        Assert.Equal(VnpayGateway.ProviderName, linked.PaymentProvider);
        var adminTransactions = (await scope.ServiceProvider.GetRequiredService<IAdminQueryService>()
            .TransactionsAsync(admin.Id, order.Id.ToString(), null, 1))!;
        Assert.Equal(payment.ProviderTransactionId, Assert.Single(adminTransactions.Items).ProviderTransactionId);
        using var adminClient = IdentityTestHelpers.CreateClient(app);
        using var adminLogin = await IdentityTestHelpers.LoginAsync(adminClient, admin);
        Assert.Contains("VNPay Sandbox", await adminClient.GetStringAsync($"/Admin/Enrollments?search={Uri.EscapeDataString(course.Title)}"));
        Assert.Contains("VNPay Sandbox", await adminClient.GetStringAsync($"/Admin/Transactions?search={order.Id}"));
        Assert.Contains("Sandbox", await adminClient.GetStringAsync("/Admin/Revenue?period=all"));
        // Source is historical Payment linkage, not the course's current paid/free flag.
        var originallyFree = Course(0); originallyFree.IsPaid = false;
        db.Enrollments.Add(new Enrollment { Course = originallyFree, StudentUserId = student.Id });
        await db.SaveChangesAsync();
        originallyFree.IsPaid = true; originallyFree.Price = 99000m; await db.SaveChangesAsync();
        var historicalFree = Assert.Single((await scope.ServiceProvider.GetRequiredService<IAdminOverviewDetailQueryService>()
            .EnrollmentsAsync(admin.Id, originallyFree.Title, null, 1))!.Items);
        Assert.Null(historicalFree.PaymentId);
        Assert.Null(historicalFree.OrderId);
        Assert.Contains("Miễn phí / Không qua thanh toán", WebUtility.HtmlDecode(await adminClient.GetStringAsync(
            $"/Admin/Enrollments?search={Uri.EscapeDataString(originallyFree.Title)}")));


    }

    [Fact]
    public async Task EnabledButMissingCredentials_CannotCreateOrdersOrFallBackToSimulation()
    {
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vnpay:UseIpn"] = "true", ["Vnpay:Enabled"] = "true", ["Vnpay:TmnCode"] = "TEST0001", ["Vnpay:HashSecret"] = "",
                ["Vnpay:PublicBaseUrl"] = "https://merchant.example.test"
            })));
        var teacher = await IdentityTestHelpers.CreateUserAsync(app, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(app, AppRoles.Student);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new Course { Title = "Unconfigured VNPAY", OwnerTeacherUserId = teacher.Id,
            Status = CourseStatus.Published, IsPaid = true, Price = 199000m };
        db.Courses.Add(course); await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<ICheckoutOrderService>();
        var preview = (await service.PreviewAsync(student.Id, course.Id)).Value!;
        Assert.False(preview.HostedEnabled);
        Assert.False(preview.SandboxEnabled);
        Assert.Equal(CheckoutCode.Disabled, (await service.CreateOrderAsync(student.Id, course.Id)).Code);
        Assert.False(await db.Orders.AnyAsync(o => o.CourseId == course.Id));
    }

    private static Dictionary<string, string> Notification(Guid id, decimal amount, string transaction, string response = "00", string status = "00")
    {
        var data = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = "TEST0001", ["vnp_TxnRef"] = id.ToString("N"),
            ["vnp_Amount"] = (amount * 100).ToString("0", CultureInfo.InvariantCulture), ["vnp_TransactionNo"] = transaction,
            ["vnp_ResponseCode"] = response, ["vnp_TransactionStatus"] = status,
            ["vnp_PayDate"] = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
        };
        Sign(data); return data;
    }
    private static void Sign(Dictionary<string, string> data)
    {
        var canonical = string.Join("&", data.Where(p => p.Key != "vnp_SecureHash").OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}"));
        data["vnp_SecureHash"] = Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(TestKey), Encoding.UTF8.GetBytes(canonical)));
    }
    private static async Task<string> Ipn(HttpClient client, Dictionary<string, string> data)
    {
        using var response = await client.GetAsync(QueryHelpers.AddQueryString("/payments/vnpay/ipn", data!));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("RspCode").GetString()!;
    }

}
