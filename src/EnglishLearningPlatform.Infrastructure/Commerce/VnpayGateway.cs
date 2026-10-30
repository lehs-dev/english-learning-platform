using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EnglishLearningPlatform.Infrastructure.Commerce;

public sealed class VnpayOptions
{
    public bool Enabled { get; set; }
    public bool UseIpn { get; set; }
    public string TmnCode { get; set; } = "";
    public string HashSecret { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";
    // Explicit server-owned browser origins, never an arbitrary ReturnUrl or forwarded header.
    public string[] BrowserBaseUrls { get; set; } = [];
    public int CheckoutMinutes { get; set; } = 15;
}

// VNPAY PAY 2.1.0. This adapter deliberately targets the sandbox endpoint only.
public sealed class VnpayGateway(IOptions<VnpayOptions> options, IHttpContextAccessor http) : IWebhookPaymentGateway
{
    public const string ProviderName = "vnpay-sandbox";
    public const string PaymentUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
    // VNPay's VND checkout settles whole dong; use the same quote everywhere.
    public static decimal QuoteAmount(decimal price) => decimal.Truncate(price);
    private readonly VnpayOptions settings = options.Value;
    public string Provider => ProviderName;
    public bool Enabled => settings.Enabled;
    public bool UsesIpn => settings.UseIpn;
    public bool IsConfigured => settings.Enabled && settings.TmnCode.Length == 8 &&
        settings.TmnCode.All(char.IsAsciiLetterOrDigit) && !string.IsNullOrWhiteSpace(settings.HashSecret) &&
        settings.CheckoutMinutes is >= 1 and <= 1440 &&
        ((!settings.UseIpn && string.IsNullOrWhiteSpace(settings.PublicBaseUrl)) ||
         Uri.TryCreate(settings.PublicBaseUrl, UriKind.Absolute, out var uri) &&
         (uri.Scheme == "https" || !settings.UseIpn && uri.Scheme == "http" && uri.IsLoopback) &&
         string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment));
    public DateTimeOffset CheckoutDeadline => DateTimeOffset.UtcNow.AddMinutes(settings.CheckoutMinutes);

    public string CreateCheckoutUrl(CheckoutOrder order)
    {
        if (!IsConfigured) throw new InvalidOperationException("VNPAY sandbox is not configured.");
        var quoted = QuoteAmount(order.Amount);
        if (order.Id == Guid.Empty || order.Currency != "VND" || quoted <= 0 ||
            order.Amount * 100 > 999999999999m || decimal.Round(order.Amount, 2) != order.Amount)
            throw new ArgumentException("Amount is outside the VNPAY PAY limit.", nameof(order));
        var ip = http.HttpContext?.Connection.RemoteIpAddress ?? IPAddress.Loopback;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Version"] = "2.1.0", ["vnp_Command"] = "pay", ["vnp_TmnCode"] = settings.TmnCode,
            ["vnp_Amount"] = (quoted * 100).ToString("0", CultureInfo.InvariantCulture),
            ["vnp_CurrCode"] = "VND", ["vnp_TxnRef"] = order.Id.ToString("N"),
            ["vnp_OrderInfo"] = $"Thanh toan khoa hoc {order.Id:N}", ["vnp_OrderType"] = "other",
            ["vnp_Locale"] = "vn", ["vnp_IpAddr"] = ip.ToString(),
            ["vnp_ReturnUrl"] = BrowserBaseUrl() + "/payments/vnpay/return",
            ["vnp_CreateDate"] = VietnamTime(order.CreatedAtUtc == default ? DateTimeOffset.UtcNow : order.CreatedAtUtc),
            ["vnp_ExpireDate"] = VietnamTime(order.ExpiresAtUtc)
        };
        var canonical = Canonical(data);
        return PaymentUrl + "?" + canonical + "&vnp_SecureHash=" + Sign(canonical);
    }

    // VNPay does not speak the project's legacy JSON webhook protocol.
    public PaymentNotification? VerifyNotification(byte[] body, string signature) => null;

    public Guid? ReadOrderId(IReadOnlyDictionary<string, string> query)
    {
        if (!VerifySignature(query) || Get(query, "vnp_TmnCode") != settings.TmnCode ||
            !Guid.TryParseExact(Get(query, "vnp_TxnRef"), "N", out var id) || id == Guid.Empty) return null;
        return id;
    }

    public PaymentNotification? ReadNotification(IReadOnlyDictionary<string, string> query)
    {
        var id = ReadOrderId(query);
        if (!id.HasValue) return null;
        var response = Get(query, "vnp_ResponseCode");
        var state = Get(query, "vnp_TransactionStatus");
        PaymentStatus status;
        if (response == "00" && state == "00") status = PaymentStatus.Succeeded;
        else if (state is "01" or "02" && response == "24") status = PaymentStatus.Cancelled;
        else if (state is "01" or "02" && response is "09" or "10" or "11" or "12" or "13" or "51" or "65" or "75" or "79" or "99") status = PaymentStatus.Failed;
        else return null; // Pending, suspicious and reversal/refund states require reconciliation, not access.
        return ReadReceipt(id.Value, query, status);
    }

    private PaymentNotification? ReadReceipt(Guid id, IReadOnlyDictionary<string, string> query, PaymentStatus status)
    {
        var rawAmount = Get(query, "vnp_Amount");
        if (Get(query, "vnp_CurrCode") is not ("" or "VND") || !Digits(rawAmount, 12) ||
            !decimal.TryParse(rawAmount, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0) return null;
        var transaction = Get(query, "vnp_TransactionNo");
        if (!Digits(transaction, 15) || status == PaymentStatus.Succeeded && transaction.All(c => c == '0')) return null;
        var eventId = transaction.All(c => c == '0')
            ? $"{Provider}:{settings.TmnCode}:unsuccessful:{id:N}"
            : $"{Provider}:{settings.TmnCode}:{transaction}";
        var payDate = Get(query, "vnp_PayDate");
        DateTimeOffset occurred = default;
        if (!string.IsNullOrEmpty(payDate))
        {
            if (!DateTime.TryParseExact(payDate, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var time)) return null;
            occurred = new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).ToUniversalTime();
        }
        if (status == PaymentStatus.Succeeded && occurred == default) return null;
        return new(id, eventId, amount / 100m, "VND", status, occurred);
    }

    private bool VerifySignature(IReadOnlyDictionary<string, string> query)
    {
        if (!IsConfigured || query.Count is 0 or > 50 || query.Any(p => !p.Key.StartsWith("vnp_", StringComparison.Ordinal) ||
                p.Key.Length > 64 || p.Value.Length > 2048 || p.Value.Any(char.IsControl))) return false;
        var hash = Get(query, "vnp_SecureHash");
        if (hash.Length != 128) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(Sign(Canonical(query)))); }
        catch (FormatException) { return false; }
    }
    private string Sign(string data) => Convert.ToHexStringLower(HMACSHA512.HashData(
        Encoding.UTF8.GetBytes(settings.HashSecret), Encoding.UTF8.GetBytes(data)));
    private string BrowserBaseUrl()
    {
        var request = http.HttpContext?.Request;
        if (request is not null)
        {
            var origin = request.Scheme + "://" + request.Host.Value;
            if (!settings.UseIpn && Uri.TryCreate(origin, UriKind.Absolute, out var local) && local.IsLoopback &&
                local.Scheme is "http" or "https") return local.GetLeftPart(UriPartial.Authority);
            foreach (var allowed in settings.BrowserBaseUrls)
            {
                if (Uri.TryCreate(allowed, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) &&
                    string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) &&
                    string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == "/" &&
                    string.Equals(uri.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase))
                    return uri.GetLeftPart(UriPartial.Authority);
            }
        }
        if (string.IsNullOrWhiteSpace(settings.PublicBaseUrl))
            throw new InvalidOperationException("Local VNPay checkout requires a loopback browser origin.");
        return settings.PublicBaseUrl.TrimEnd('/');
    }
    private static string Canonical(IEnumerable<KeyValuePair<string, string>> values) => string.Join("&", values
        .Where(p => p.Key is not ("vnp_SecureHash" or "vnp_SecureHashType") && p.Value.Length > 0)
        .OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}"));
    private static string Get(IReadOnlyDictionary<string, string> values, string key) => values.TryGetValue(key, out var value) ? value : "";
    private static bool Digits(string value, int max) => value.Length > 0 && value.Length <= max && value.All(char.IsAsciiDigit);
    private static string VietnamTime(DateTimeOffset time) => time.ToOffset(TimeSpan.FromHours(7)).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
}
