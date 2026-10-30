using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class VnpayLocalReturnTests(CheckoutTestFactory factory) : IClassFixture<CheckoutTestFactory>
{
    private static readonly string TestKey = new('v', 32);
    [Theory]
    [InlineData("00", "00", PaymentStatus.Succeeded, true, "199000", false)]
    [InlineData("24", "02", PaymentStatus.Cancelled, false, "199000", false)]
    [InlineData("51", "02", PaymentStatus.Failed, false, "199000", false)]
    [InlineData("00", "00", PaymentStatus.Succeeded, true, "99999.98", false)]
    [InlineData("00", "00", PaymentStatus.Succeeded, true, "99999.98", true)]
    public async Task LocalSignedReturn_SettlesOnce_WithoutTunnelIpnOrQueryApi(string response, string state, PaymentStatus expected, bool enrolled, string rawPrice, bool legacyFractionalQuote)
    {
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["Vnpay:Enabled"] = "true", ["Vnpay:UseIpn"] = "false", ["Vnpay:TmnCode"] = "TEST0001",
                ["Vnpay:HashSecret"] = TestKey, ["Vnpay:PublicBaseUrl"] = ""
            })));
        var teacher = await IdentityTestHelpers.CreateUserAsync(app, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(app, AppRoles.Student);
        var other = await IdentityTestHelpers.CreateUserAsync(app, AppRoles.Student);
        var admin = await IdentityTestHelpers.CreateUserAsync(app, AppRoles.Admin);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var price = decimal.Parse(rawPrice, CultureInfo.InvariantCulture);
        var quoted = decimal.Truncate(price);
        var course = new Course { Title = $"Local VNPay {Guid.NewGuid():N}", OwnerTeacherUserId = teacher.Id,
            Status = CourseStatus.Published, IsPaid = true, Price = price };
        db.Courses.Add(course); await db.SaveChangesAsync();
        var preview = await scope.ServiceProvider.GetRequiredService<ICheckoutOrderService>().PreviewAsync(student.Id, course.Id);
        Assert.Equal(quoted, preview.Value!.Amount);
        using var client = IdentityTestHelpers.CreateClient(app);
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Checkout?courseId={course.Id}");
        using var created = await client.PostAsync("/Checkout/CreateOrder", new FormUrlEncodedContent(new Dictionary<string,string>
            { ["CourseId"] = course.Id.ToString(), ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var order = await db.Orders.SingleAsync(o => o.CourseId == course.Id);
        Assert.Equal(quoted, order.Amount);
        if (legacyFractionalQuote) { order.Amount = price; await db.SaveChangesAsync(); }
        var data = new Dictionary<string,string>
        {
            ["vnp_TmnCode"] = "TEST0001", ["vnp_TxnRef"] = order.Id.ToString("N"), ["vnp_Amount"] = (quoted * 100).ToString("0", CultureInfo.InvariantCulture),
            ["vnp_ResponseCode"] = response, ["vnp_TransactionStatus"] = state,
            ["vnp_TransactionNo"] = Random.Shared.Next(10000000,99999999).ToString(CultureInfo.InvariantCulture),
            ["vnp_PayDate"] = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
        };
        Sign(data);
        var tampered = new Dictionary<string,string>(data) { ["vnp_ResponseCode"] = "99" };
        using var bad = await client.GetAsync(QueryHelpers.AddQueryString("/payments/vnpay/return", tampered!));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.False(await db.Payments.AnyAsync(p => p.OrderId == order.Id));
        // Even a correctly signed receipt cannot underpay by a whole dong.
        var underpaid = new Dictionary<string,string>(data);
        underpaid.Remove("vnp_SecureHash");
        underpaid["vnp_Amount"] = ((quoted - 1) * 100).ToString("0", CultureInfo.InvariantCulture);
        Sign(underpaid);
        using var underReturn = await client.GetAsync(QueryHelpers.AddQueryString("/payments/vnpay/return", underpaid!));
        using var underResult = await client.GetAsync(underReturn.Headers.Location);
        Assert.False(await db.Payments.AnyAsync(p => p.OrderId == order.Id));

        // Guest can carry a signed response, but no settlement occurs before an owner logs in.
        using var guest = IdentityTestHelpers.CreateClient(app);
        var url = QueryHelpers.AddQueryString("/payments/vnpay/return", data!);
        using var guestReturn = await guest.GetAsync(url);
        using var challenge = await guest.GetAsync(guestReturn.Headers.Location);
        Assert.Contains("/Account/Login", challenge.Headers.Location!.ToString());
        Assert.False(await db.Payments.AnyAsync(p => p.OrderId == order.Id));
        using var otherLogin = await IdentityTestHelpers.LoginAsync(guest, other);
        using var notOwner = await guest.GetAsync(guestReturn.Headers.Location);
        Assert.Equal(HttpStatusCode.NotFound, notOwner.StatusCode);
        Assert.False(await db.Payments.AnyAsync(p => p.OrderId == order.Id));
        // Deferred response survives login and is verified again for its actual owner.
        using var ownerLogin = await IdentityTestHelpers.LoginAsync(guest, student);
        using var settled = await guest.GetAsync(guestReturn.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, settled.StatusCode);
        var receipt = await db.Payments.SingleAsync(p => p.OrderId == order.Id);
        Assert.Equal(expected, receipt.Status);
        Assert.Equal(quoted, receipt.Amount);
        await db.Entry(order).ReloadAsync();
        Assert.Equal(quoted, order.Amount);
        Assert.Equal(enrolled, await db.Enrollments.AnyAsync(e => e.CourseId == course.Id && e.StudentUserId == student.Id));
        using var repeatedReturn = await client.GetAsync(url);
        using var repeatedResult = await client.GetAsync(repeatedReturn.Headers.Location);
        Assert.Single(await db.Payments.Where(p => p.OrderId == order.Id).ToListAsync());
        if (enrolled)
        {
            Assert.DoesNotContain("href=\"https://sandbox.vnpayment.vn/paymentv2", await repeatedResult.Content.ReadAsStringAsync());
            var rows = (await scope.ServiceProvider.GetRequiredService<IAdminQueryService>().TransactionsAsync(admin.Id, order.Id.ToString(), null, 1))!;
            Assert.Equal(PaymentStatus.Succeeded, Assert.Single(rows.Items).Status);
            var service = scope.ServiceProvider.GetRequiredService<ICheckoutOrderService>();
            Assert.Equal(CheckoutCode.AlreadyEnrolled, (await service.CreateOrderAsync(student.Id, course.Id)).Code);
        }
    }
    private static void Sign(Dictionary<string,string> data)
    {
        var canonical = string.Join("&", data.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}"));
        data["vnp_SecureHash"] = Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(TestKey),Encoding.UTF8.GetBytes(canonical)));
    }
}
