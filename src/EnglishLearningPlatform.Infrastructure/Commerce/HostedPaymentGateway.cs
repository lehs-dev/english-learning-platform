using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Domain.Enums;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EnglishLearningPlatform.Infrastructure.Commerce;

public sealed class HostedPaymentOptions
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "Hosted";
    public string CheckoutUrl { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";
    public string SigningSecret { get; set; } = "";
    public int CheckoutMinutes { get; set; } = 30;
}

// Adapter for an external hosted gateway that implements the documented HMAC contract.
// No browser endpoint can sign a success notification.
public sealed class HostedPaymentGateway(IOptions<HostedPaymentOptions> options, IHostEnvironment environment) : IPaymentGateway
{
    private readonly HostedPaymentOptions settings = options.Value;
    public string Provider => settings.Provider;
    public bool IsConfigured => settings.Enabled && settings.SigningSecret.Length >= 32 &&
        settings.Provider.Length is > 0 and <= 64 && settings.Provider.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') &&
        !string.Equals(settings.Provider, LocalSandboxGateway.ProviderName, StringComparison.OrdinalIgnoreCase) &&
        settings.CheckoutMinutes is >= 1 and <= 1440 && ValidUrl(settings.CheckoutUrl) && ValidUrl(settings.PublicBaseUrl);
    public DateTimeOffset CheckoutDeadline => DateTimeOffset.UtcNow.AddMinutes(settings.CheckoutMinutes);

    private bool ValidUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
        (uri.Scheme == Uri.UriSchemeHttps || (environment.IsDevelopment() && uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp));

    public string CreateCheckoutUrl(CheckoutOrder order)
    {
        if (!IsConfigured) throw new InvalidOperationException("Hosted payment gateway is not configured.");
        var baseUrl = settings.PublicBaseUrl.TrimEnd('/');
        var values = new Dictionary<string, string?>
        {
            ["orderId"] = order.Id.ToString("D"), ["amount"] = order.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            ["currency"] = order.Currency, ["expiresAt"] = order.ExpiresAtUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ["returnUrl"] = $"{baseUrl}/Checkout/Result/{order.Id:D}", ["webhookUrl"] = $"{baseUrl}/payments/webhook"
        };
        // Field order is part of the gateway protocol. URLs and all values are encoded independently.
        var canonical = string.Join("&", values.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));
        values["signature"] = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.SigningSecret), Encoding.UTF8.GetBytes(canonical)));
        return QueryHelpers.AddQueryString(settings.CheckoutUrl, values);
    }

    public PaymentNotification? VerifyNotification(byte[] body, string signature)
    {
        if (!IsConfigured || body.Length is 0 or > 16384 || signature.Length != 64) return null;
        byte[] supplied;
        try { supplied = Convert.FromHexString(signature); }
        catch (FormatException) { return null; }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.SigningSecret), body);
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied)) return null;
        try
        {
            var notification = JsonSerializer.Deserialize<PaymentNotification>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (notification is null || notification.OrderId == Guid.Empty ||
                string.IsNullOrWhiteSpace(notification.ProviderTransactionId) || notification.ProviderTransactionId.Length > 128 ||
                notification.ProviderTransactionId.Any(char.IsControl) || notification.Currency != "VND" ||
                notification.Amount <= 0 || notification.Amount > 9999999999999999.99m || decimal.Round(notification.Amount, 2) != notification.Amount ||
                notification.Status is not (PaymentStatus.Succeeded or PaymentStatus.Failed) ||
                notification.OccurredAtUtc == default || notification.OccurredAtUtc > DateTimeOffset.UtcNow.AddMinutes(5)) return null;
            return notification;
        }
        catch (JsonException) { return null; }
    }
}
