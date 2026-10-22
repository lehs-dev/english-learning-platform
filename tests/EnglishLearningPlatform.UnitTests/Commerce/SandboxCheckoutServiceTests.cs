using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Commerce;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Commerce;

public sealed class SandboxCheckoutServiceTests
{
    private static readonly Guid StudentId = Guid.NewGuid();
    private static readonly Guid OrderId = Guid.NewGuid();

    [Fact]
    public async Task DisabledSandbox_DoesNotReadAnOrderOrCreateACallback()
    {
        var gateway = new StubSandboxGateway { IsConfigured = false };
        var orders = new StubOrderService(Order());
        var confirmations = new StubConfirmationService();

        var result = await new SandboxCheckoutService(gateway, orders, confirmations)
            .SimulateAsync(StudentId, OrderId, SandboxScenario.Success);

        Assert.Equal(CheckoutCode.Disabled, result);
        Assert.Equal(0, orders.Reads);
        Assert.Equal(0, gateway.CallbacksCreated);
        Assert.Null(confirmations.Callback);
    }

    [Fact]
    public async Task UnknownScenario_IsRejectedBeforeReadingAnOrder()
    {
        var gateway = new StubSandboxGateway();
        var orders = new StubOrderService(Order());

        var result = await new SandboxCheckoutService(gateway, orders, new StubConfirmationService())
            .SimulateAsync(StudentId, OrderId, (SandboxScenario)999);

        Assert.Equal(CheckoutCode.InvalidCallback, result);
        Assert.Equal(0, orders.Reads);
        Assert.Equal(0, gateway.CallbacksCreated);
    }

    [Theory]
    [InlineData(CheckoutCode.Forbidden)]
    [InlineData(CheckoutCode.NotFound)]
    [InlineData(CheckoutCode.IntegrationError)]
    public async Task OrderAccessFailure_IsPreservedWithoutSigning(CheckoutCode failure)
    {
        var gateway = new StubSandboxGateway();
        var orders = new StubOrderService(new(failure));
        var confirmations = new StubConfirmationService();

        var result = await new SandboxCheckoutService(gateway, orders, confirmations)
            .SimulateAsync(StudentId, OrderId, SandboxScenario.Success);

        Assert.Equal(failure, result);
        Assert.Equal(StudentId, orders.StudentId);
        Assert.Equal(OrderId, orders.OrderId);
        Assert.Equal(0, gateway.CallbacksCreated);
        Assert.Null(confirmations.Callback);
    }

    [Fact]
    public async Task HostedOrder_CannotBeSignedByTheSandbox()
    {
        var gateway = new StubSandboxGateway();
        var confirmations = new StubConfirmationService();
        var service = new SandboxCheckoutService(gateway,
            new StubOrderService(Order(provider: "Hosted")), confirmations);

        Assert.Equal(CheckoutCode.Forbidden,
            await service.SimulateAsync(StudentId, OrderId, SandboxScenario.Success));
        Assert.Equal(0, gateway.CallbacksCreated);
        Assert.Null(confirmations.Callback);
    }

    [Theory]
    [InlineData(false, false, CheckoutCode.NotPurchasable)]
    [InlineData(false, true, CheckoutCode.NotPurchasable)]
    [InlineData(true, true, CheckoutCode.AlreadyEnrolled)]
    public async Task UnsettledOrder_UsesCheckoutEligibilityBeforeSimulation(
        bool canCheckout, bool canLearn, CheckoutCode expected)
    {
        var gateway = new StubSandboxGateway();
        var confirmations = new StubConfirmationService();
        var service = new SandboxCheckoutService(gateway,
            new StubOrderService(Order(canCheckout: canCheckout, canLearn: canLearn)), confirmations);

        Assert.Equal(expected, await service.SimulateAsync(StudentId, OrderId, SandboxScenario.Success));
        Assert.Equal(0, gateway.CallbacksCreated);
        Assert.Null(confirmations.Callback);
    }

    [Theory]
    [InlineData(SandboxScenario.Success, PaymentStatus.Succeeded, CheckoutCode.Allowed)]
    [InlineData(SandboxScenario.Failed, PaymentStatus.Failed, CheckoutCode.Conflict)]
    [InlineData(SandboxScenario.Cancelled, PaymentStatus.Cancelled, CheckoutCode.IntegrationError)]
    public async Task SettledOrder_StillAllowsReplayThroughTheConfirmationPipeline(
        SandboxScenario scenario, PaymentStatus previousStatus, CheckoutCode outcome)
    {
        var gateway = new StubSandboxGateway();
        var orders = new StubOrderService(Order(status: previousStatus, canCheckout: false, canLearn: true));
        var confirmations = new StubConfirmationService { Result = outcome };
        using var cancellation = new CancellationTokenSource();

        var result = await new SandboxCheckoutService(gateway, orders, confirmations)
            .SimulateAsync(StudentId, OrderId, scenario, cancellation.Token);

        Assert.Equal(outcome, result);
        Assert.Equal(1, gateway.CallbacksCreated);
        Assert.Equal(scenario, gateway.Scenario);
        Assert.Same(gateway.Callback, confirmations.Callback);
        Assert.Equal(cancellation.Token, orders.CancellationToken);
        Assert.Equal(cancellation.Token, confirmations.CancellationToken);
    }

    [Fact]
    public async Task EligibleOrder_IsSignedAndConfirmedWithoutChangingTheCallback()
    {
        var gateway = new StubSandboxGateway();
        var confirmations = new StubConfirmationService();

        var result = await new SandboxCheckoutService(gateway, new StubOrderService(Order()), confirmations)
            .SimulateAsync(StudentId, OrderId, SandboxScenario.Success);

        Assert.Equal(CheckoutCode.Allowed, result);
        Assert.Equal(1, gateway.CallbacksCreated);
        Assert.Same(gateway.Callback, confirmations.Callback);
    }

    [Fact]
    public async Task Cancellation_PreventsOrderReadsAndSigning()
    {
        var gateway = new StubSandboxGateway();
        var orders = new StubOrderService(Order());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SandboxCheckoutService(gateway, orders, new StubConfirmationService())
                .SimulateAsync(StudentId, OrderId, SandboxScenario.Success, cancellation.Token));

        Assert.Equal(0, orders.Reads);
        Assert.Equal(0, gateway.CallbacksCreated);
    }

    private static CheckoutOutcome<OrderCheckout> Order(string provider = "local-sandbox",
        PaymentStatus? status = null, bool canCheckout = true, bool canLearn = false) =>
        new(CheckoutCode.Allowed, new(OrderId, Guid.NewGuid(), "Course", "Teacher", 100_000m, "VND",
            status, canLearn, true, provider, CanCheckout: canCheckout));

    private sealed class StubSandboxGateway : ISandboxPaymentGateway
    {
        public string Provider => "local-sandbox";
        public bool IsConfigured { get; init; } = true;
        public DateTimeOffset CheckoutDeadline => throw new NotSupportedException();
        public int CallbacksCreated { get; private set; }
        public SandboxScenario? Scenario { get; private set; }
        public SignedPaymentCallback? Callback { get; private set; }
        public SignedPaymentCallback CreateCallback(OrderCheckout order, SandboxScenario scenario)
        {
            CallbacksCreated++;
            Scenario = scenario;
            Callback = new(new(order.Id, order.Amount, order.Currency, Provider, "local-event",
                PaymentStatus.Succeeded, DateTimeOffset.UtcNow), "signature");
            return Callback;
        }
        public string CreateCheckoutUrl(CheckoutOrder order) => throw new NotSupportedException();
        public SignedPaymentCallback Sign(PaymentCallback data) => throw new NotSupportedException();
        public bool Verify(SignedPaymentCallback callback) => throw new NotSupportedException();
    }

    private sealed class StubOrderService(CheckoutOutcome<OrderCheckout> order) : ICheckoutOrderService
    {
        public int Reads { get; private set; }
        public Guid StudentId { get; private set; }
        public Guid OrderId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public Task<CheckoutOutcome<OrderCheckout>> GetOrderAsync(Guid userId, Guid orderId, CancellationToken ct = default)
        {
            Reads++;
            StudentId = userId;
            OrderId = orderId;
            CancellationToken = ct;
            return Task.FromResult(order);
        }
        public Task<CheckoutOutcome<CourseCheckout>> PreviewAsync(Guid userId, Guid courseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CheckoutOutcome<OrderCheckout>> CreateOrderAsync(Guid userId, Guid courseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CheckoutOutcome<TransactionHistoryPage>> HistoryAsync(Guid userId, int page = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CheckoutStart> StartAsync(Guid userId, Guid courseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LearningOutcome<CheckoutPage>> GetAsync(Guid userId, Guid orderId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubConfirmationService : IPaymentConfirmationService
    {
        public CheckoutCode Result { get; init; } = CheckoutCode.Allowed;
        public SignedPaymentCallback? Callback { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public Task<CheckoutCode> ProcessCallbackAsync(SignedPaymentCallback callback, CancellationToken ct = default)
        {
            Callback = callback;
            CancellationToken = ct;
            return Task.FromResult(Result);
        }
        public Task<PaymentConfirmation> ConfirmAsync(byte[] body, string signature, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
