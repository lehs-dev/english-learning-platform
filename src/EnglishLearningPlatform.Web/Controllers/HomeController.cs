using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index(string section = "courses")
    {
        ViewData["ActiveSection"] = section is "courses" or "exams" or "assessment" or "about" or "dictionary"
            ? section : "courses";
        return View();
    }
    public IActionResult Error() => View();
}
