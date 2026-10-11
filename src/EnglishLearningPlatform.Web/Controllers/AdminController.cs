using System.Security.Claims;
using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminController(IAdminQueryService queries) : Controller
{
    private Guid? UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet]
    public IActionResult Index() => RedirectToAction(nameof(Transactions));

    [HttpGet]
    public async Task<IActionResult> Transactions(string? search, PaymentStatus? status, int page = 1, CancellationToken ct = default)
    {
        if (!UserId.HasValue) return Challenge();
        if (!ModelState.IsValid || status.HasValue && !Enum.IsDefined(status.Value)) return BadRequest();
        ViewData["ActiveSection"] = "admin";
        ViewData["Status"] = status;
        var result = await queries.TransactionsAsync(UserId.Value, search, status, page, ct);
        return result is null ? Forbid() : View(result);
    }

    [HttpGet]
    public async Task<IActionResult> Users(string? search, AccountStatus? status, int page = 1, CancellationToken ct = default)
    {
        if (!UserId.HasValue) return Challenge();
        if (!ModelState.IsValid || status.HasValue && !Enum.IsDefined(status.Value)) return BadRequest();
        ViewData["ActiveSection"] = "admin";
        ViewData["Status"] = status;
        var result = await queries.UsersAsync(UserId.Value, search, status, page, ct);
        return result is null ? Forbid() : View(result);
    }
}
