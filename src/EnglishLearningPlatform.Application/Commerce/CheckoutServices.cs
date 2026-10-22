using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Application.Commerce;

public interface ICheckoutOrderService
{
    Task<CheckoutOutcome<CourseCheckout>> PreviewAsync(Guid userId, Guid courseId, CancellationToken ct = default);
    Task<CheckoutOutcome<OrderCheckout>> CreateOrderAsync(Guid userId, Guid courseId, CancellationToken ct = default);
    Task<CheckoutOutcome<OrderCheckout>> GetOrderAsync(Guid userId, Guid orderId, CancellationToken ct = default);
    Task<CheckoutOutcome<TransactionHistoryPage>> HistoryAsync(Guid userId, int page = 1, CancellationToken ct = default);
    Task<CheckoutStart> StartAsync(Guid userId, Guid courseId, CancellationToken ct = default);
    Task<LearningOutcome<CheckoutPage>> GetAsync(Guid userId, Guid orderId, CancellationToken ct = default);
}

public interface IPaymentConfirmationService
{
    Task<CheckoutCode> ProcessCallbackAsync(SignedPaymentCallback callback, CancellationToken ct = default);
    Task<PaymentConfirmation> ConfirmAsync(byte[] body, string signature, CancellationToken ct = default);
}

public interface ISandboxCheckoutService
{
    Task<CheckoutCode> SimulateAsync(Guid userId, Guid orderId, SandboxScenario scenario, CancellationToken ct = default);
}
