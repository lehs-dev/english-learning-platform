using EnglishLearningPlatform.Application.Commerce;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[ApiController, Route("payments")]
public sealed class PaymentsController(ICheckoutService checkout, ILogger<PaymentsController> logger) : ControllerBase
{
    // No cookie/CSRF trust for server callbacks: all fields must carry a valid gateway HMAC.
    [HttpPost("webhook"), AllowAnonymous]
    public async Task<IActionResult> Webhook(SignedPaymentCallback callback, CancellationToken ct)
    {
        var result = await checkout.ProcessCallbackAsync(callback, ct);
        logger.LogInformation("Payment callback outcome: {Code}", result); // Never log body/signature/key.
        return result switch
        {
            CheckoutCode.Allowed => Ok(new { code = "accepted" }),
            CheckoutCode.Disabled => NotFound(),
            CheckoutCode.Conflict => Conflict(new { code = "conflicting_event" }),
            CheckoutCode.IntegrationError => StatusCode(503, new { code = "retry_later" }),
            _ => BadRequest(new { code = "invalid_callback" })
        };
    }
}
