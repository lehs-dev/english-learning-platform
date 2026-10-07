using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

public sealed class HomeController(EnglishLearningPlatform.Application.Learning.ICourseService courses) : Controller
{
    public async Task<IActionResult> Index(string section = "courses", CancellationToken ct = default)
    {
        if (section == "courses")
        {
            ViewData["ActiveSection"] = "courses";
            return View("~/Views/Courses/Index.cshtml", await courses.CatalogAsync(new(), ct));
        }
        ViewData["ActiveSection"] = section is "courses" or "exams" or "assessment" or "about" or "dictionary"
            ? section : "courses";
        return View();
    }
    public IActionResult Error() => View();
}
