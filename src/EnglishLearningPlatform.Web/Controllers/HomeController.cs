using System.Security.Claims;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

public sealed class HomeController(ICourseCatalogService courses) : Controller
{
    public async Task<IActionResult> Index(string? section = null, CancellationToken ct = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var role = User.FindFirstValue(ClaimTypes.Role);
            if (RoleNavigation.HasRoleWorkspace(role))
                return this.RedirectToRoleWorkspace(role);
        }

        if (section == "courses")
        {
            return RedirectToAction("Index", "Courses");
        }

        ViewData["ActiveSection"] = section is "exams" or "assessment" or "about" or "dictionary"
            ? section : null;

        var catalog = await courses.CatalogAsync(new CatalogQuery { Page = 1 }, ct);
        return View(catalog);
    }

    public IActionResult Error() => View();
}
