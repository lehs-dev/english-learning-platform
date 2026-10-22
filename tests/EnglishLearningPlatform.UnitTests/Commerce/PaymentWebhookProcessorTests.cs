using System.Text;
using System.Text.Json;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Web.Commerce;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Commerce;

public sealed class PaymentWebhookProcessorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SignedPaymentCallback Callback = new(
        new(Guid.NewGuid(), 100_000m, "VND", "local-sandbox", "local-event", PaymentStatus.Succeeded,
            DateTimeOffset.UtcNow), "local-signature");

    [Theory]
    [InlineData(CheckoutCode.Allowed, 200, "accepted")]
    [InlineData(CheckoutCode.Disabled, 404, null)]
    [InlineData(CheckoutCode.Conflict, 409, "conflicting_event")]
    [InlineData(CheckoutCode.IntegrationError, 503, "retry_later")]
    [InlineData(CheckoutCode.InvalidCallback, 400, "invalid_callback")]
    public async Task LocalEnvelope_PreservesCallbackResponses(CheckoutCode outcome, int status, string? code)
    {
        var checkout = new StubCheckoutService { LocalResult = outcome };
        var request = Request(JsonSerializer.SerializeToUtf8Bytes(Callback, JsonOptions));
        using var cancellation = new CancellationTokenSource();

        var result = await Processor(checkout).ProcessAsync(request, cancellation.Token);

        AssertResponse(result, status, code);
        Assert.Equal(Callback, checkout.Callback);
        Assert.Null(checkout.HostedBody);
        Assert.Equal(cancellation.Token, checkout.CancellationToken);
    }

    [Theory]
    [InlineData(PaymentConfirmation.Accepted, 200, "accepted")]
    [InlineData(PaymentConfirmation.NotFound, 404, null)]
    [InlineData(PaymentConfirmation.Conflict, 409, "conflicting_event")]
    [InlineData(PaymentConfirmation.Unavailable, 503, "retry_later")]
    [InlineData(PaymentConfirmation.Invalid, 400, "invalid_callback")]
    public async Task HostedSignature_PreservesRawBodyAndResponses(PaymentConfirmation outcome, int status, string? code)
    {
        var checkout = new StubCheckoutService { HostedResult = outcome };
        var body = Encoding.UTF8.GetBytes(" { \"amount\":100000,\n\"currency\":\"VND\" } ");
        var request = Request(body);
        request.Headers["X-Payment-Signature"] = "hosted-signature";
        using var cancellation = new CancellationTokenSource();

        var result = await Processor(checkout).ProcessAsync(request, cancellation.Token);

        AssertResponse(result, status, code);
        Assert.Equal(body, checkout.HostedBody);
        Assert.Equal("hosted-signature", checkout.HostedSignature);
        Assert.Null(checkout.Callback);
        Assert.Equal(cancellation.Token, checkout.CancellationToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hosted-signature")]
    public async Task SignatureHeader_OnLocalEnvelope_UsesOnlyHostedVerification(string signature)
    {
        var checkout = new StubCheckoutService();
        var body = JsonSerializer.SerializeToUtf8Bytes(Callback, JsonOptions);
        var request = Request(body);
        request.Headers["X-Payment-Signature"] = signature;

        await Processor(checkout).ProcessAsync(request);

        Assert.Null(checkout.Callback);
        Assert.Equal(body, checkout.HostedBody);
        Assert.Equal(signature, checkout.HostedSignature);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{\"data\":null,\"signature\":42}")]
    public async Task MalformedUnsignedBody_IsRejectedWithoutCallingCheckout(string body)
    {
        var checkout = new StubCheckoutService();

        var result = await Processor(checkout).ProcessAsync(Request(Encoding.UTF8.GetBytes(body)));

        AssertResponse(result, 400, "invalid_callback");
        Assert.Null(checkout.Callback);
        Assert.Null(checkout.HostedBody);
    }

    [Fact]
    public async Task UnsignedBodyWithoutLocalEnvelope_UsesHostedVerificationWithEmptySignature()
    {
        var checkout = new StubCheckoutService();
        var body = Encoding.UTF8.GetBytes("{\"orderId\":\"hosted-order\"}");

        await Processor(checkout).ProcessAsync(Request(body));

        Assert.Null(checkout.Callback);
        Assert.Equal(body, checkout.HostedBody);
        Assert.Equal("", checkout.HostedSignature);
    }

    [Theory]
    [InlineData(16384, 200)]
    [InlineData(16385, 413)]
    public async Task FragmentedBody_EnforcesExactSizeLimit(int length, int status)
    {
        var checkout = new StubCheckoutService();
        var body = Enumerable.Repeat((byte)' ', length).ToArray();
        var request = Request(body);
        request.Body = new FragmentedReadStream(body);
        request.Headers["X-Payment-Signature"] = "hosted-signature";

        var result = await Processor(checkout).ProcessAsync(request);

        AssertResponse(result, status, status == 200 ? "accepted" : null);
        if (status == 200) Assert.Equal(body, checkout.HostedBody);
        else Assert.Null(checkout.HostedBody);
        Assert.Null(checkout.Callback);
    }

    [Fact]
    public async Task CancelledRead_PropagatesCancellationWithoutCallingCheckout()
    {
        var checkout = new StubCheckoutService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Processor(checkout).ProcessAsync(Request([1]), cancellation.Token));

        Assert.Null(checkout.Callback);
        Assert.Null(checkout.HostedBody);
    }

    private static PaymentWebhookProcessor Processor(StubCheckoutService checkout) =>
        new(checkout, NullLogger<PaymentWebhookProcessor>.Instance);

    private static HttpRequest Request(byte[] body)
    {
        var request = new DefaultHttpContext().Request;
        request.Body = new MemoryStream(body);
        return request;
    }

    private static void AssertResponse(IActionResult result, int status, string? code)
    {
        if (code is null)
        {
            Assert.Equal(status, Assert.IsAssignableFrom<StatusCodeResult>(result).StatusCode);
            return;
        }
        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, JsonSerializer.SerializeToElement(response.Value).GetProperty("code").GetString());
    }

    private sealed class FragmentedReadStream(byte[] body) : MemoryStream(body)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 127)], cancellationToken);
    }

    private sealed class StubCheckoutService : ICheckoutService
    {
        public CheckoutCode LocalResult { get; init; } = CheckoutCode.Allowed;
        public PaymentConfirmation HostedResult { get; init; } = PaymentConfirmation.Accepted;
        public SignedPaymentCallback? Callback { get; private set; }
        public byte[]? HostedBody { get; private set; }
        public string? HostedSignature { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<CheckoutCode> ProcessCallbackAsync(SignedPaymentCallback callback, CancellationToken ct = default)
        {
            Callback = callback;
            CancellationToken = ct;
            return Task.FromResult(LocalResult);
        }

        public Task<PaymentConfirmation> ConfirmAsync(byte[] body, string signature, CancellationToken ct = default)
        {
            HostedBody = body;
            HostedSignature = signature;
            CancellationToken = ct;
            return Task.FromResult(HostedResult);
        }

        public Task<CheckoutOutcome<CourseCheckout>> PreviewAsync(Guid userId, Guid courseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CheckoutOutcome<OrderCheckout>> CreateOrderAsync(Guid userId, Guid courseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CheckoutOutcome<OrderCheckout>> GetOrderAsync(Guid userId, Guid orderId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CheckoutOutcome<TransactionHistoryPage>> HistoryAsync(Guid userId, int page = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CheckoutCode> SimulateAsync(Guid userId, Guid orderId, SandboxScenario scenario, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CheckoutStart> StartAsync(Guid userId, Guid courseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LearningOutcome<CheckoutPage>> GetAsync(Guid userId, Guid orderId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
