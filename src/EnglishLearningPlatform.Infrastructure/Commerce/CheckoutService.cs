using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Infrastructure.Commerce;

// Compatibility entry point for callers that need the complete checkout workflow.
public sealed class CheckoutService(ICheckoutOrderService orders,
    IPaymentConfirmationService confirmations, ISandboxCheckoutService sandbox) : ICheckoutService
{
    public Task<CheckoutOutcome<CourseCheckout>> PreviewAsync(Guid userId, Guid courseId, CancellationToken ct = default) =>
        orders.PreviewAsync(userId, courseId, ct);

    public Task<CheckoutOutcome<OrderCheckout>> CreateOrderAsync(Guid userId, Guid courseId, CancellationToken ct = default) =>
        orders.CreateOrderAsync(userId, courseId, ct);

    public Task<CheckoutOutcome<OrderCheckout>> GetOrderAsync(Guid userId, Guid orderId, CancellationToken ct = default) =>
        orders.GetOrderAsync(userId, orderId, ct);

    public Task<CheckoutOutcome<TransactionHistoryPage>> HistoryAsync(Guid userId, int page = 1, CancellationToken ct = default) =>
        orders.HistoryAsync(userId, page, ct);

    public Task<CheckoutStart> StartAsync(Guid userId, Guid courseId, CancellationToken ct = default) =>
        orders.StartAsync(userId, courseId, ct);

    public Task<LearningOutcome<CheckoutPage>> GetAsync(Guid userId, Guid orderId, CancellationToken ct = default) =>
        orders.GetAsync(userId, orderId, ct);

    public Task<CheckoutCode> SimulateAsync(Guid userId, Guid orderId, SandboxScenario scenario, CancellationToken ct = default) =>
        sandbox.SimulateAsync(userId, orderId, scenario, ct);

    public Task<CheckoutCode> ProcessCallbackAsync(SignedPaymentCallback callback, CancellationToken ct = default) =>
        confirmations.ProcessCallbackAsync(callback, ct);

    public Task<PaymentConfirmation> ConfirmAsync(byte[] body, string signature, CancellationToken ct = default) =>
        confirmations.ConfirmAsync(body, signature, ct);
}
