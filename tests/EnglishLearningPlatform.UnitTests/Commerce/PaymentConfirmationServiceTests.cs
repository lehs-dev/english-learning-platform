using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Commerce;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Commerce;

public sealed class PaymentConfirmationServiceTests
{
    private static readonly PaymentCallback ValidData = new(Guid.NewGuid(), 100_000m, "VND", "local-sandbox",
        "local-event", PaymentStatus.Succeeded, DateTimeOffset.UtcNow);

    [Fact]
    public async Task DisabledHostedGateway_IsUnavailableBeforeVerificationOrPersistence()
    {
        var hosted = new StubHostedGateway { IsConfigured = false };

        var result = await Service(hosted, new StubSandboxGateway()).ConfirmAsync([1, 2], "signature");

        Assert.Equal(PaymentConfirmation.Unavailable, result);
        Assert.Equal(0, hosted.Verifications);
    }

    [Fact]
    public async Task InvalidHostedSignature_IsRejectedBeforePersistenceWithOriginalBody()
    {
        var hosted = new StubHostedGateway();
        byte[] body = [1, 2, 3];

        var result = await Service(hosted, new StubSandboxGateway()).ConfirmAsync(body, "signature");

        Assert.Equal(PaymentConfirmation.Invalid, result);
        Assert.Same(body, hosted.Body);
        Assert.Equal("signature", hosted.Signature);
        Assert.Equal(1, hosted.Verifications);
    }

    [Fact]
    public async Task DisabledSandbox_IsRejectedBeforeVerificationOrPersistence()
    {
        var local = new StubSandboxGateway { IsConfigured = false };

        var result = await Service(new StubHostedGateway(), local)
            .ProcessCallbackAsync(new(ValidData, "signature"));

        Assert.Equal(CheckoutCode.Disabled, result);
        Assert.Equal(0, local.Verifications);
    }

    [Fact]
    public async Task InvalidLocalSignature_IsRejectedBeforeInspectingThePayload()
    {
        var local = new StubSandboxGateway { ValidSignature = false };
        var callback = new SignedPaymentCallback(null!, "signature");

        var result = await Service(new StubHostedGateway(), local).ProcessCallbackAsync(callback);

        Assert.Equal(CheckoutCode.InvalidCallback, result);
        Assert.Equal(1, local.Verifications);
        Assert.Same(callback, local.Callback);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("order")]
    [InlineData("currency")]
    [InlineData("amount")]
    [InlineData("empty-transaction")]
    [InlineData("invalid-transaction")]
    [InlineData("status")]
    [InlineData("occurred-at")]
    public async Task InvalidSignedLocalPayload_IsRejectedBeforePersistence(string invalidField)
    {
        var data = invalidField switch
        {
            "provider" => ValidData with { Provider = "Hosted" },
            "order" => ValidData with { OrderId = Guid.Empty },
            "currency" => ValidData with { Currency = "USD" },
            "amount" => ValidData with { Amount = 0m },
            "empty-transaction" => ValidData with { ProviderTransactionId = "" },
            "invalid-transaction" => ValidData with { ProviderTransactionId = "event\n" },
            "status" => ValidData with { Status = PaymentStatus.Pending },
            "occurred-at" => ValidData with { OccurredAtUtc = default },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField))
        };

        var result = await Service(new StubHostedGateway(), new StubSandboxGateway())
            .ProcessCallbackAsync(new(data, "signature"));

        Assert.Equal(CheckoutCode.InvalidCallback, result);
    }

    [Fact]
    public async Task Cancellation_PreventsBothHostedAndLocalVerification()
    {
        var hosted = new StubHostedGateway();
        var local = new StubSandboxGateway();
        var service = Service(hosted, local);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ConfirmAsync([1], "signature", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ProcessCallbackAsync(new(ValidData, "signature"), cancellation.Token));

        Assert.Equal(0, hosted.Verifications);
        Assert.Equal(0, local.Verifications);
    }

    // Any accidental persistence before authentication/validation fails immediately.
    private static PaymentConfirmationService Service(IWebhookPaymentGateway hosted, ISandboxPaymentGateway local) =>
        new(null!, null!, hosted, local, null!);

    private sealed class StubHostedGateway : IWebhookPaymentGateway
    {
        public string Provider => "Hosted";
        public bool IsConfigured { get; init; } = true;
        public DateTimeOffset CheckoutDeadline => throw new NotSupportedException();
        public int Verifications { get; private set; }
        public byte[]? Body { get; private set; }
        public string? Signature { get; private set; }
        public PaymentNotification? VerifyNotification(byte[] body, string signature)
        {
            Verifications++;
            Body = body;
            Signature = signature;
            return null;
        }
        public string CreateCheckoutUrl(CheckoutOrder order) => throw new NotSupportedException();
    }

    private sealed class StubSandboxGateway : ISandboxPaymentGateway
    {
        public string Provider => "local-sandbox";
        public bool IsConfigured { get; init; } = true;
        public bool ValidSignature { get; init; } = true;
        public DateTimeOffset CheckoutDeadline => throw new NotSupportedException();
        public int Verifications { get; private set; }
        public SignedPaymentCallback? Callback { get; private set; }
        public bool Verify(SignedPaymentCallback callback)
        {
            Verifications++;
            Callback = callback;
            return ValidSignature;
        }
        public SignedPaymentCallback CreateCallback(OrderCheckout order, SandboxScenario scenario) => throw new NotSupportedException();
        public SignedPaymentCallback Sign(PaymentCallback data) => throw new NotSupportedException();
        public string CreateCheckoutUrl(CheckoutOrder order) => throw new NotSupportedException();
    }
}
