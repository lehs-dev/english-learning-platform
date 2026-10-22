using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Commerce;

public sealed record CheckoutOrder(Guid Id, decimal Amount, string Currency, DateTimeOffset ExpiresAtUtc);
public sealed record PaymentNotification(Guid OrderId, string ProviderTransactionId, decimal Amount,
    string Currency, PaymentStatus Status, DateTimeOffset OccurredAtUtc);
public sealed record CheckoutStart(LearningAccessResult Access, Guid CourseId, Guid? OrderId = null,
    string? CheckoutUrl = null, bool AlreadyEnrolled = false, string? Error = null);
public sealed record CheckoutPage(Guid Id, Guid CourseId, string CourseTitle, decimal Amount, string Currency,
    PaymentStatus Status, bool Enrolled, DateTimeOffset? ExpiresAtUtc, string? CheckoutUrl, bool IsExpired = false);
public enum PaymentConfirmation { Accepted, Invalid, Conflict, NotFound, Unavailable }

public interface IPaymentGateway
{
    string Provider { get; }
    bool IsConfigured { get; }
    DateTimeOffset CheckoutDeadline { get; }
    string CreateCheckoutUrl(CheckoutOrder order);
}

public interface IWebhookPaymentGateway : IPaymentGateway
{
    PaymentNotification? VerifyNotification(byte[] body, string signature);
}

public interface ISandboxPaymentGateway : IPaymentGateway
{
    SignedPaymentCallback CreateCallback(OrderCheckout order, SandboxScenario scenario);
    SignedPaymentCallback Sign(PaymentCallback data);
    bool Verify(SignedPaymentCallback callback);
}

public interface ICheckoutService : ICheckoutOrderService, IPaymentConfirmationService, ISandboxCheckoutService
{
}
