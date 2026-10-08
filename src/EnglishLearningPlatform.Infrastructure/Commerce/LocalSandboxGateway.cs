using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EnglishLearningPlatform.Infrastructure.Commerce;

// A process-local simulator, not an external payment provider. Never registers a known/default key.
public sealed class LocalSandboxGateway : IPaymentGateway
{
    public const string ProviderName = "local-sandbox";
    private readonly byte[] _key;
    public bool IsEnabled { get; }
    public string Provider => ProviderName;
    public bool IsConfigured => IsEnabled;
    public DateTimeOffset CheckoutDeadline => DateTimeOffset.UtcNow.AddMinutes(30);
    public string CreateCheckoutUrl(CheckoutOrder order) => $"/Checkout/Order/{order.Id:D}";
    public PaymentNotification? VerifyNotification(byte[] body, string signature) => null;

    public LocalSandboxGateway(IHostEnvironment environment, IConfiguration configuration)
    {
        IsEnabled = environment.IsDevelopment() && configuration.GetValue<bool>("CheckoutSandbox:Enabled");
        _key = IsEnabled ? RandomNumberGenerator.GetBytes(32) : [];
    }

    public SignedPaymentCallback CreateCallback(OrderCheckout order, SandboxScenario scenario)
    {
        var status = scenario switch
        {
            SandboxScenario.Success => PaymentStatus.Succeeded,
            SandboxScenario.Failed => PaymentStatus.Failed,
            SandboxScenario.Cancelled => PaymentStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        return Sign(new(order.Id, order.Amount, order.Currency, ProviderName, $"local-{order.Id:N}", status, order.CreatedAtUtc));
    }

    // Server-only operation, intentionally not exposed by any HTTP route.
    public SignedPaymentCallback Sign(PaymentCallback data)
    {
        if (!IsEnabled) throw new InvalidOperationException("Local checkout sandbox is disabled.");
        return new(data, Convert.ToHexString(HMACSHA256.HashData(_key, Canonical(data))));
    }

    public bool Verify(SignedPaymentCallback callback)
    {
        if (!IsEnabled || callback?.Data is null || string.IsNullOrWhiteSpace(callback.Signature)) return false;
        try
        {
            var supplied = Convert.FromHexString(callback.Signature);
            return supplied.Length == 32 && CryptographicOperations.FixedTimeEquals(supplied,
                HMACSHA256.HashData(_key, Canonical(callback.Data)));
        }
        catch (FormatException) { return false; }
    }

    private static byte[] Canonical(PaymentCallback data) => Encoding.UTF8.GetBytes(string.Join("\n",
        data.OrderId.ToString("N"), data.Amount.ToString("G29", CultureInfo.InvariantCulture),
        data.Currency, data.Provider, data.ProviderTransactionId, ((int)data.Status).ToString(CultureInfo.InvariantCulture),
        data.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
}
