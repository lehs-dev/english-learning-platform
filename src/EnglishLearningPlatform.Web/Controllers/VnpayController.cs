using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Commerce;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EnglishLearningPlatform.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class VnpayController(IVnpayPaymentService payments) : Controller
{
    // Provider-to-server notification. Authentication is the VNPay HMAC, never a browser cookie.
    [AllowAnonymous, HttpGet("/payments/vnpay/ipn")]
    public async Task<IActionResult> Ipn(CancellationToken ct)
    {
        var query = ReadQuery();
        var result = query is null ? new VnpayIpnResult("97", "Invalid request") : await payments.ConfirmVnpayAsync(query, ct);
        // VNPay requires exact JSON casing; ASP.NET's normal JSON naming policy is camelCase.
        return new JsonResult(new Dictionary<string, string> { ["RspCode"] = result.RspCode, ["Message"] = result.Message });
    }

    [AllowAnonymous, HttpGet("/payments/vnpay/return")]
    public IActionResult Return()
    {
        var query = ReadQuery();
        var id = query is null ? null : payments.ReadVnpayReturn(query);
        if (!id.HasValue) return BadRequest();
        TempData["VnpayReturnedOrder"] = id.Value.ToString("N");
        TempData["VnpayReturnPayload"] = JsonSerializer.Serialize(query);
        // The encrypted TempData carries the signed response across login if necessary.
        // Checkout.Result enforces Student + ownership before settlement in local mode.
        // An expired session resumes the short Result URL, never a long callback query in Login.
        return RedirectToAction("Result", "Checkout", new { id = id.Value });
    }

    private Dictionary<string, string>? ReadQuery()
    {
        if (Request.QueryString.Value?.Length > 16384 || Request.Query.Count > 50 ||
            Request.Query.Any(p => p.Value.Count != 1)) return null;
        return Request.Query.ToDictionary(p => p.Key, p => p.Value[0] ?? "", StringComparer.Ordinal);
    }
}
