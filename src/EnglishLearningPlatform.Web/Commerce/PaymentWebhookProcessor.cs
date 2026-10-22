using System.Text.Json;
using EnglishLearningPlatform.Application.Commerce;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Commerce;

public sealed class PaymentWebhookProcessor(
    IPaymentConfirmationService checkout, ILogger<PaymentWebhookProcessor> logger) : IPaymentWebhookProcessor
{
    private const int MaximumBodyLength = 16384;
    private const string SignatureHeader = "X-Payment-Signature";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IActionResult> ProcessAsync(HttpRequest request, CancellationToken ct = default)
    {
        var buffer = new byte[MaximumBodyLength + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await request.Body.ReadAsync(buffer.AsMemory(length), ct);
            if (read == 0) break;
            length += read;
        }
        if (length > MaximumBodyLength) return new StatusCodeResult(StatusCodes.Status413PayloadTooLarge);
        var body = buffer[..length];
        if (!request.Headers.ContainsKey(SignatureHeader))
        {
            try
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.ValueKind == JsonValueKind.Object &&
                    json.RootElement.TryGetProperty("data", out _) && json.RootElement.TryGetProperty("signature", out _))
                {
                    var callback = JsonSerializer.Deserialize<SignedPaymentCallback>(body, JsonOptions);
                    if (callback is null) return InvalidCallback();
                    var code = await checkout.ProcessCallbackAsync(callback, ct);
                    logger.LogInformation("Local callback outcome: {Code}", code);
                    return code switch
                    {
                        CheckoutCode.Allowed => new OkObjectResult(new { code = "accepted" }),
                        CheckoutCode.Disabled => new NotFoundResult(),
                        CheckoutCode.Conflict => new ConflictObjectResult(new { code = "conflicting_event" }),
                        CheckoutCode.IntegrationError => RetryLater(),
                        _ => InvalidCallback()
                    };
                }
            }
            catch (JsonException) { return InvalidCallback(); }
        }
        var result = await checkout.ConfirmAsync(body, request.Headers[SignatureHeader].ToString(), ct);
        logger.LogInformation("Hosted callback outcome: {Code}", result);
        return result switch
        {
            PaymentConfirmation.Accepted => new OkObjectResult(new { code = "accepted" }),
            PaymentConfirmation.NotFound => new NotFoundResult(),
            PaymentConfirmation.Conflict => new ConflictObjectResult(new { code = "conflicting_event" }),
            PaymentConfirmation.Unavailable => RetryLater(),
            _ => InvalidCallback()
        };
    }

    private static BadRequestObjectResult InvalidCallback() => new(new { code = "invalid_callback" });
    private static ObjectResult RetryLater() => new(new { code = "retry_later" }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
}
