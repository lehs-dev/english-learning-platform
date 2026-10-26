using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Commerce;

public interface IPaymentWebhookProcessor
{
    Task<IActionResult> ProcessAsync(HttpRequest request, CancellationToken ct = default);
}
