using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EnglishLearningPlatform.Web.Controllers;
public sealed class CoursesController(ICourseService courses) : Controller
{
    private Guid? UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Index(CatalogQuery query, CancellationToken ct)
    {
        ViewData["ActiveSection"] = "courses";
        if (!ModelState.IsValid) ViewData["QueryNotice"] = "Một số bộ lọc không hợp lệ đã được bỏ qua.";
        var result = await courses.CatalogAsync(query, ct);
        ModelState.Clear();
        return View(result);
    }
    [HttpGet, AllowAnonymous, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        ViewData["ActiveSection"] = "courses";
        var page = await courses.PublicDetailAsync(id, UserId, ct);
        return page is null ? NotFound() : View(page);
    }
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AuthorizationPolicies.Student)]
    public async Task<IActionResult> EnrollFree(Guid id, CancellationToken ct)
    {
        if (!UserId.HasValue) return Challenge();
        var result = await courses.EnrollFreeAsync(UserId.Value, id, ct);
        return result.Access switch
        {
            LearningAccessResult.Allowed => RedirectToAction("Course", "Learning", new { id }),
            LearningAccessResult.NotFound => NotFound(),
            _ => Forbid()
        };
    }
}
