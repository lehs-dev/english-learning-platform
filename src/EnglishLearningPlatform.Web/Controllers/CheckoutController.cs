using System.Security.Claims;
using System.Text.Json;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Application.Learning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Student)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CheckoutController(ICheckoutService checkout, ILogger<CheckoutController> logger) : Controller
{
    private Guid? UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private IActionResult Learn(Guid courseId) => RedirectToAction("Course", "Learning", new { id = courseId });
    private IActionResult Denied(LearningAccessResult access) => access == LearningAccessResult.NotFound ? NotFound() : Forbid();
    [HttpGet]
    public async Task<IActionResult> Index(Guid courseId, CancellationToken ct)
    {
        if (!UserId.HasValue) return Challenge();
        ViewData["ActiveSection"] = "courses";
        var result = await checkout.PreviewAsync(UserId.Value, courseId, ct);
        if (result.Code == CheckoutCode.IntegrationError) TempData["Error"] = Message(result.Code);
        return result.Code switch
        {
            CheckoutCode.Allowed => View(result.Value), CheckoutCode.AlreadyEnrolled => Learn(courseId),
            CheckoutCode.Forbidden => Forbid(), CheckoutCode.NotFound => NotFound(), _ => View("Unavailable")
        };
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrder([Bind("CourseId")] CreateOrderRequest request, CancellationToken ct)
    {
        if (!UserId.HasValue) return Challenge();
        if (!ModelState.IsValid || request.CourseId == Guid.Empty) return BadRequest();
        var result = await checkout.CreateOrderAsync(UserId.Value, request.CourseId, ct);
        if (result.Code == CheckoutCode.Allowed) return RedirectToAction(result.Value!.IsSandbox ? nameof(Order) : nameof(Result), new { id = result.Value.Id });
        if (result.Code == CheckoutCode.AlreadyEnrolled) return Learn(request.CourseId);
        if (result.Code == CheckoutCode.Forbidden) return Forbid();
        if (result.Code == CheckoutCode.NotFound) return NotFound();
        TempData["Error"] = Message(result.Code);
        return RedirectToAction(nameof(Index), new { courseId = request.CourseId });
    }
    // Keep main's hosted contract; this endpoint never creates a local sandbox Order.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        if (!UserId.HasValue) return Challenge();
        var result = await checkout.StartAsync(UserId.Value, id, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        if (result.AlreadyEnrolled) return Learn(result.CourseId);
        if (result.Error is not null)
        { TempData["Error"] = result.Error; return RedirectToAction("Details", "Courses", new { id }); }
        return RedirectToAction(nameof(Result), new { id = result.OrderId });
    }
    [HttpGet]
    public Task<IActionResult> Order(Guid id, CancellationToken ct) => RenderOrder(id, ct);
    [HttpGet]
    public Task<IActionResult> Result(Guid id, CancellationToken ct) => RenderOrder(id, ct);
    private async Task<IActionResult> RenderOrder(Guid id, CancellationToken ct)
    {
        if (!UserId.HasValue) return Challenge();
        ViewData["ActiveSection"] = "courses";
        var result = await checkout.GetOrderAsync(UserId.Value, id, ct);
        if (result.Code == CheckoutCode.IntegrationError)
        { Response.StatusCode = 503; TempData["Error"] = Message(result.Code); return View("Unavailable"); }
        return result.Code == CheckoutCode.Allowed ? View("Order", result.Value)
            : result.Code == CheckoutCode.Forbidden ? Forbid() : NotFound();
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Simulate(Guid id, string scenario, CancellationToken ct)
    {
        if (!UserId.HasValue) return Challenge();
        SandboxScenario? selected = scenario switch
        { "success" => SandboxScenario.Success, "failed" => SandboxScenario.Failed, "cancelled" => SandboxScenario.Cancelled, _ => null };
        if (!selected.HasValue) return BadRequest();
        var result = await checkout.SimulateAsync(UserId.Value, id, selected.Value, ct);
        if (result == CheckoutCode.Forbidden) return Forbid();
        if (result == CheckoutCode.NotFound) return NotFound();
        if (result != CheckoutCode.Allowed) TempData["Error"] = Message(result);
        return RedirectToAction(nameof(Order), new { id });
    }
    // One webhook route. Hosted raw-byte HMAC and local envelope are never cross-verified.
    [AllowAnonymous, HttpPost("/payments/webhook"), IgnoreAntiforgeryToken, RequestSizeLimit(16384)]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        var buffer = new byte[16385]; var length = 0;
        while (length < buffer.Length)
        {
            var read = await Request.Body.ReadAsync(buffer.AsMemory(length), ct);
            if (read == 0) break;
            length += read;
        }
        if (length > 16384) return StatusCode(413);
        var body = buffer[..length];
        if (!Request.Headers.ContainsKey("X-Payment-Signature"))
        {
            try
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("data", out _) && json.RootElement.TryGetProperty("signature", out _))
                {
                    var callback = JsonSerializer.Deserialize<SignedPaymentCallback>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (callback is null) return BadRequest(new { code = "invalid_callback" });
                    var code = await checkout.ProcessCallbackAsync(callback, ct);
                    logger.LogInformation("Local callback outcome: {Code}", code);
                    return code switch
                    {
                        CheckoutCode.Allowed => Ok(new { code = "accepted" }), CheckoutCode.Disabled => NotFound(),
                        CheckoutCode.Conflict => Conflict(new { code = "conflicting_event" }),
                        CheckoutCode.IntegrationError => StatusCode(503, new { code = "retry_later" }),
                        _ => BadRequest(new { code = "invalid_callback" })
                    };
                }
            }
            catch (JsonException) { return BadRequest(new { code = "invalid_callback" }); }
        }
        var result = await checkout.ConfirmAsync(body, Request.Headers["X-Payment-Signature"].ToString(), ct);
        logger.LogInformation("Hosted callback outcome: {Code}", result);
        return result switch
        {
            PaymentConfirmation.Accepted => Ok(new { code = "accepted" }), PaymentConfirmation.NotFound => NotFound(),
            PaymentConfirmation.Conflict => Conflict(new { code = "conflicting_event" }),
            PaymentConfirmation.Unavailable => StatusCode(503, new { code = "retry_later" }),
            _ => BadRequest(new { code = "invalid_callback" })
        };
    }
    private static string Message(CheckoutCode code) => code switch
    {
        CheckoutCode.Disabled => "Thanh toán thử nghiệm hiện chưa khả dụng. Vui lòng thử lại sau.",
        CheckoutCode.NotPurchasable => "Khóa học hiện không đủ điều kiện mua. Vui lòng quay lại danh mục.",
        CheckoutCode.AlreadyEnrolled => "Bạn đã có quyền học. Chọn Vào học để tiếp tục.",
        CheckoutCode.Conflict => "Lần thanh toán này đã kết thúc hoặc dữ liệu giao dịch không khớp. Trạng thái đã lưu được giữ nguyên.",
        _ => "Chưa thể xử lý thanh toán thử nghiệm. Vui lòng thử lại."
    };
}
