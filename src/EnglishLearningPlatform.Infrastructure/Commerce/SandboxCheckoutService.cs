using EnglishLearningPlatform.Application.Commerce;

namespace EnglishLearningPlatform.Infrastructure.Commerce;

// Simulation checks ownership and scenario availability before creating a signed callback.
public sealed class SandboxCheckoutService(ISandboxPaymentGateway local,
    ICheckoutOrderService orders, IPaymentConfirmationService confirmations) : ISandboxCheckoutService
{
    public async Task<CheckoutCode> SimulateAsync(Guid userId, Guid orderId, SandboxScenario scenario, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!local.IsConfigured) return CheckoutCode.Disabled;
        if (!Enum.IsDefined(scenario)) return CheckoutCode.InvalidCallback;
        var owned = await orders.GetOrderAsync(userId, orderId, ct);
        if (owned.Code != CheckoutCode.Allowed) return owned.Code;
        // A local key/scenario can never sign or settle an externally hosted Order.
        if (!owned.Value!.IsSandbox) return CheckoutCode.Forbidden;
        if (!owned.Value.Status.HasValue && !owned.Value.CanCheckout) return CheckoutCode.NotPurchasable;
        if (owned.Value.CanLearn && !owned.Value.Status.HasValue) return CheckoutCode.AlreadyEnrolled;
        return await confirmations.ProcessCallbackAsync(local.CreateCallback(owned.Value, scenario), ct);
    }
}
