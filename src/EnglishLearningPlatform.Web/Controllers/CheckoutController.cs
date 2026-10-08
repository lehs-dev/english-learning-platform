using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Commerce;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Student)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CheckoutController(ICheckoutService checkout) : Controller
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private IActionResult Learn(Guid courseId) => RedirectToAction("Course", "Learning", new { id = courseId });

    [HttpGet]
    public async Task<IActionResult> Index(Guid courseId, CancellationToken ct)
    {
        ViewData["ActiveSection"] = "courses";
        var result = await checkout.PreviewAsync(UserId, courseId, ct);
        return result.Code switch
        {
            CheckoutCode.Allowed => View(result.Value),
            CheckoutCode.AlreadyEnrolled => Learn(courseId),
            CheckoutCode.Forbidden => Forbid(),
            CheckoutCode.NotFound => NotFound(),
            _ => View("Unavailable")
        };
    }

    // Khang's integration endpoint. No buyer, price, currency or payment status is bound from the form.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrder([Bind("CourseId")] CreateOrderRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request.CourseId == Guid.Empty) return BadRequest();
        var result = await checkout.CreateOrderAsync(UserId, request.CourseId, ct);
        if (result.Code == CheckoutCode.Allowed) return RedirectToAction(nameof(Order), new { id = result.Value!.Id });
        if (result.Code == CheckoutCode.AlreadyEnrolled) return Learn(request.CourseId);
        if (result.Code == CheckoutCode.Forbidden) return Forbid();
        if (result.Code == CheckoutCode.NotFound) return NotFound();
        TempData["Error"] = Message(result.Code);
        return RedirectToAction(nameof(Index), new { courseId = request.CourseId });
    }

    [HttpGet]
    public async Task<IActionResult> Order(Guid id, CancellationToken ct)
    {
        ViewData["ActiveSection"] = "courses";
        var result = await checkout.GetOrderAsync(UserId, id, ct);
        return result.Code == CheckoutCode.Allowed ? View(result.Value)
            : result.Code == CheckoutCode.Forbidden ? Forbid() : NotFound();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Simulate(Guid id, string scenario, CancellationToken ct)
    {
        SandboxScenario? selected = scenario switch
        {
            "success" => SandboxScenario.Success,
            "failed" => SandboxScenario.Failed,
            "cancelled" => SandboxScenario.Cancelled,
            _ => null
        };
        if (!selected.HasValue) return BadRequest();
        var result = await checkout.SimulateAsync(UserId, id, selected.Value, ct);
        if (result == CheckoutCode.Forbidden) return Forbid();
        if (result == CheckoutCode.NotFound) return NotFound();
        if (result != CheckoutCode.Allowed) TempData["Error"] = Message(result);
        return RedirectToAction(nameof(Order), new { id });
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
