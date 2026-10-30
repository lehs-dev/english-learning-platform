using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Commerce;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Commerce;

public sealed class VnpayGatewayTests
{
    [Theory]
    [InlineData(5050)]
    [InlineData(8080)]
    public void LocalMode_ReturnsToCurrentPort_WithoutPublicUrl(int port)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http"; context.Request.Host = new HostString("localhost", port);
        var gateway = new VnpayGateway(Options.Create(new VnpayOptions { Enabled = true,
            TmnCode = "TEST0001", HashSecret = TestKey }), new HttpContextAccessor { HttpContext = context });
        Assert.True(gateway.IsConfigured);
        var url = gateway.CreateCheckoutUrl(new(Guid.NewGuid(),199000m,"VND",DateTimeOffset.UtcNow.AddMinutes(15)));
        Assert.Equal($"http://localhost:{port}/payments/vnpay/return", QueryHelpers.ParseQuery(new Uri(url).Query)["vnp_ReturnUrl"]);
    }

    [Theory]
    [InlineData("http", "localhost:8080", "http://localhost:8080")]
    [InlineData("http", "127.0.0.1:8080", "http://127.0.0.1:8080")]
    [InlineData("https", "merchant.example.test", "https://merchant.example.test")]
    [InlineData("https", "attacker.example.test", "https://merchant.example.test")]
    public void Return_UsesOnlyConfiguredBrowserOrigins_KeepingLocalCookieDomain(string scheme, string host, string expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        context.Request.Headers["X-Forwarded-Host"] = "attacker.example.test";
        var gateway = new VnpayGateway(Options.Create(new VnpayOptions
        {
            Enabled = true, TmnCode = "TEST0001", HashSecret = TestKey,
            PublicBaseUrl = "https://merchant.example.test",
            BrowserBaseUrls = ["http://localhost:8080", "http://127.0.0.1:8080"]
        }), new HttpContextAccessor { HttpContext = context });
        var url = gateway.CreateCheckoutUrl(new(Guid.NewGuid(), 199000m, "VND", DateTimeOffset.UtcNow.AddMinutes(15)));
        var query = QueryHelpers.ParseQuery(new Uri(url).Query);
        Assert.Equal(expected + "/payments/vnpay/return", query["vnp_ReturnUrl"]);
    }

    // Synthetic test-only key; no merchant credentials are stored in source.
    private static readonly string TestKey = new('v', 32);
    private static VnpayGateway Gateway(bool enabled = true, string url = "https://merchant.example.test") => new(
        Options.Create(new VnpayOptions { Enabled = enabled, TmnCode = "TEST0001", HashSecret = TestKey, PublicBaseUrl = url }),
        new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

    [Fact]
    public void Checkout_UsesWholeDongQuote_SortedEncodedHmac_AndVietnamExpiry()
    {
        var id = Guid.NewGuid();
        var expires = new DateTimeOffset(2026, 12, 31, 17, 5, 0, TimeSpan.Zero);
        var url = Gateway().CreateCheckoutUrl(new(id, 99999.98m, "VND", expires, expires.AddMinutes(-15)));
        Assert.StartsWith(VnpayGateway.PaymentUrl + "?", url);
        var query = QueryHelpers.ParseQuery(new Uri(url).Query).ToDictionary(p => p.Key, p => p.Value.ToString());
        Assert.Equal("9999900", query["vnp_Amount"]);
        Assert.Equal("20270101000500", query["vnp_ExpireDate"]);
        Assert.Equal("20261231235000", query["vnp_CreateDate"]);
        Assert.Equal(id.ToString("N"), query["vnp_TxnRef"]);
        Assert.Equal("2.1.0", query["vnp_Version"]);
        Assert.Equal("https://merchant.example.test/payments/vnpay/return", query["vnp_ReturnUrl"]);
        Assert.DoesNotContain(TestKey, url);
        var signedData = url.Split('?')[1].Split("&vnp_SecureHash=")[0];
        Assert.Equal(query.Keys.Where(k => k != "vnp_SecureHash").Order(StringComparer.Ordinal),
            signedData.Split('&').Select(p => p.Split('=')[0]));
        Assert.Equal(Hash(signedData), query["vnp_SecureHash"]);
    }

    [Theory]
    [InlineData("http://merchant.example.test")]
    [InlineData("https://user:password@merchant.example.test")]
    [InlineData("https://merchant.example.test?next=other")]
    public void InvalidPublicAddress_CannotStartPayment(string address) => Assert.False(Gateway(url: address).IsConfigured);

    [Fact]
    public void DisabledGateway_AndOutOfRangeAmounts_FailClosed()
    {
        Assert.False(Gateway(false).IsConfigured);
        Assert.Throws<InvalidOperationException>(() => Gateway(false).CreateCheckoutUrl(new(Guid.NewGuid(), 100, "VND", DateTimeOffset.UtcNow)));
        foreach (var amount in new[] { 0m, -1m, 1.001m, 10000000000m })
            Assert.Throws<ArgumentException>(() => Gateway().CreateCheckoutUrl(new(Guid.NewGuid(), amount, "VND", DateTimeOffset.UtcNow)));
        Assert.Throws<ArgumentException>(() => Gateway().CreateCheckoutUrl(new(Guid.NewGuid(), 100, "USD", DateTimeOffset.UtcNow)));
    }

    [Theory]
    [InlineData("00", "00", PaymentStatus.Succeeded)]
    [InlineData("24", "02", PaymentStatus.Cancelled)]
    [InlineData("51", "02", PaymentStatus.Failed)]
    [InlineData("13", "01", PaymentStatus.Failed)]
    public void SignedCallback_MapsFinalStates_AndConvertsVietnamDateToUtc(string response, string state, PaymentStatus expected)
    {
        var data = Callback(response, state);
        var result = Gateway().ReadNotification(data);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Status);
        Assert.Equal(199000m, result.Amount);
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 17, 0, 0, TimeSpan.Zero), result.OccurredAtUtc);
        Assert.StartsWith("vnpay-sandbox:TEST0001:", result.ProviderTransactionId);
        Assert.Null(Gateway().VerifyNotification(Encoding.UTF8.GetBytes("{}"), data["vnp_SecureHash"]));
    }

    [Theory]
    [InlineData("00", "01")]
    [InlineData("00", "04")]
    [InlineData("07", "07")]
    [InlineData("99", "05")]
    public void UnresolvedOrReversedCallbacks_DoNotBecomePaidReceipts(string response, string state)
        => Assert.Null(Gateway().ReadNotification(Callback(response, state)));

    [Fact]
    public void Signature_BindsAllFields_IncludingEncodedText_AndRequiresCorrectMerchant()
    {
        var data = Callback("00", "00");
        Assert.NotNull(Gateway().ReadNotification(data));
        data["vnp_Amount"] = "100";
        Assert.Null(Gateway().ReadNotification(data));
        data = Callback("00", "00");
        data["vnp_TmnCode"] = "OTHER001";
        Sign(data);
        Assert.Null(Gateway().ReadNotification(data));
        data = Callback("00", "00");
        data["vnp_SecureHash"] = new string('z', 128);
        Assert.Null(Gateway().ReadNotification(data));
    }

    [Fact]
    public void SuccessRequiresReceiptTimeAndTransactionId_ButFailuresWithZeroIdRemainOrderSpecific()
    {
        var data = Callback("00", "00");
        data.Remove("vnp_PayDate"); Sign(data);
        Assert.Null(Gateway().ReadNotification(data));
        data = Callback("00", "00"); data["vnp_TransactionNo"] = "0"; Sign(data);
        Assert.Null(Gateway().ReadNotification(data));
        data = Callback("24", "02"); data["vnp_TransactionNo"] = "0"; data.Remove("vnp_PayDate"); Sign(data);
        var first = Gateway().ReadNotification(data)!;
        Assert.Equal(default, first.OccurredAtUtc);
        data["vnp_TxnRef"] = Guid.NewGuid().ToString("N"); Sign(data);
        Assert.NotEqual(first.ProviderTransactionId, Gateway().ReadNotification(data)!.ProviderTransactionId);
    }

    private static Dictionary<string, string> Callback(string response, string state)
    {
        var data = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = "TEST0001", ["vnp_TxnRef"] = Guid.NewGuid().ToString("N"),
            ["vnp_Amount"] = "19900000", ["vnp_ResponseCode"] = response, ["vnp_TransactionStatus"] = state,
            ["vnp_TransactionNo"] = "123456", ["vnp_PayDate"] = "20270101000000",
            ["vnp_OrderInfo"] = "Thanh toan: Đọc / nghe & học"
        };
        Sign(data); return data;
    }
    private static void Sign(Dictionary<string, string> data) => data["vnp_SecureHash"] = Hash(string.Join("&", data
        .Where(p => p.Key != "vnp_SecureHash").OrderBy(p => p.Key, StringComparer.Ordinal)
        .Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}")));
    private static string Hash(string text) => Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(TestKey), Encoding.UTF8.GetBytes(text)));
}
