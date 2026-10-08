using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Application.Learning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Student)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CheckoutController(ICheckoutService checkout) : Controller
{
    private Guid? UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private IActionResult Denied(LearningAccessResult access) => access == LearningAccessResult.NotFound ? NotFound() : Forbid();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        if (!UserId.HasValue) return Challenge();
        var result = await checkout.StartAsync(UserId.Value, id, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        if (result.AlreadyEnrolled) return RedirectToAction("Course", "Learning", new { id = result.CourseId });
        if (result.Error is not null)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction("Details", "Courses", new { id });
        }
        return RedirectToAction(nameof(Result), new { id = result.OrderId });
    }

    [HttpGet]
    public async Task<IActionResult> Result(Guid id, CancellationToken ct)
    {
        if (!UserId.HasValue) return Challenge();
        var result = await checkout.GetAsync(UserId.Value, id, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Denied(result.Access);
    }

    [AllowAnonymous, HttpPost("/payments/webhook"), IgnoreAntiforgeryToken, RequestSizeLimit(16384)]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        var buffer = new byte[16385];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await Request.Body.ReadAsync(buffer.AsMemory(length), ct);
            if (read == 0) break;
            length += read;
        }
        if (length > 16384) return StatusCode(StatusCodes.Status413PayloadTooLarge);
        var result = await checkout.ConfirmAsync(buffer[..length], Request.Headers["X-Payment-Signature"].ToString(), ct);
        return result switch
        {
            PaymentConfirmation.Accepted => Ok(),
            PaymentConfirmation.NotFound => NotFound(),
            PaymentConfirmation.Conflict => Conflict(),
            PaymentConfirmation.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable),
            _ => BadRequest()
        };
    }
}
