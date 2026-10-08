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
    string Currency, bool SandboxEnabled, bool HostedEnabled = false);
public sealed record OrderCheckout(Guid Id, Guid CourseId, string Title, string Teacher, decimal Amount,
    string Currency, PaymentStatus? Status, bool CanLearn, bool SandboxEnabled,
    string Provider = "local-sandbox", DateTimeOffset? ExpiresAtUtc = null,
    DateTimeOffset CreatedAtUtc = default, string? CheckoutUrl = null, bool CanCheckout = true)
{
    public bool IsSandbox => Provider == "local-sandbox";
}

// Only CourseId is accepted from the purchase form; price and buyer are server-owned.
public sealed class CreateOrderRequest
{
    [Required] public Guid CourseId { get; set; }
}

public enum SandboxScenario { Success, Failed, Cancelled }
public sealed record PaymentCallback(Guid OrderId, decimal Amount, string Currency,
    string Provider, string ProviderTransactionId, PaymentStatus Status, DateTimeOffset OccurredAtUtc = default);
public sealed record SignedPaymentCallback([Required] PaymentCallback Data,
    [Required] string Signature);
