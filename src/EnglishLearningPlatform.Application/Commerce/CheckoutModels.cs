using System.ComponentModel.DataAnnotations;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Commerce;

public enum CheckoutCode
{
    Allowed, AlreadyEnrolled, Forbidden, NotFound, NotPurchasable,
    Disabled, InvalidCallback, Conflict, IntegrationError
}

public sealed record CheckoutOutcome<T>(CheckoutCode Code, T? Value = default);
public sealed record CourseCheckout(Guid CourseId, string Title, string Teacher, decimal Amount,
    string Currency, bool SandboxEnabled);
public sealed record OrderCheckout(Guid Id, Guid CourseId, string Title, string Teacher, decimal Amount,
    string Currency, PaymentStatus? Status, bool CanLearn, bool SandboxEnabled);

// Only CourseId is accepted from the purchase form; price and buyer are server-owned.
public sealed class CreateOrderRequest
{
    [Required] public Guid CourseId { get; set; }
}

public enum SandboxScenario { Success, Failed, Cancelled }
public sealed record PaymentCallback(Guid OrderId, decimal Amount, string Currency,
    string Provider, string ProviderTransactionId, PaymentStatus Status);
public sealed record SignedPaymentCallback([Required] PaymentCallback Data,
    [Required] string Signature);

public interface ICheckoutService
{
    Task<CheckoutOutcome<CourseCheckout>> PreviewAsync(Guid userId, Guid courseId, CancellationToken ct = default);
    Task<CheckoutOutcome<OrderCheckout>> CreateOrderAsync(Guid userId, Guid courseId, CancellationToken ct = default);
    Task<CheckoutOutcome<OrderCheckout>> GetOrderAsync(Guid userId, Guid orderId, CancellationToken ct = default);
    Task<CheckoutCode> SimulateAsync(Guid userId, Guid orderId, SandboxScenario scenario, CancellationToken ct = default);
    Task<CheckoutCode> ProcessCallbackAsync(SignedPaymentCallback callback, CancellationToken ct = default);
}

public interface IPaymentGateway
{
    bool IsEnabled { get; }
    SignedPaymentCallback CreateCallback(OrderCheckout order, SandboxScenario scenario);
    bool Verify(SignedPaymentCallback callback);
}
