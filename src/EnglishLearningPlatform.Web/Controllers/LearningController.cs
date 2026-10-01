using System.Security.Claims;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

// Các trang Learning hiện chỉ có khung UI. Mọi route nội dung đã được kiểm tra
// quyền trước khi render, để owner module thêm nội dung/use case sau này.
[Authorize(Roles = AppRoles.Student + "," + AppRoles.Teacher)]
public sealed class LearningController(ILearningAccessService learningAccessService) : Controller
{
    [HttpGet]
    public IActionResult Index() => Dashboard();

    [HttpGet]
    public Task<IActionResult> Course(Guid id, CancellationToken cancellationToken) =>
        OpenResourceAsync(LearningResourceType.Course, id, LearningOperation.ViewContent, cancellationToken);

    [HttpGet]
    public Task<IActionResult> Module(Guid id, CancellationToken cancellationToken) =>
        OpenResourceAsync(LearningResourceType.Module, id, LearningOperation.ViewContent, cancellationToken);

    [HttpGet]
    public Task<IActionResult> Lesson(Guid id, CancellationToken cancellationToken) =>
        OpenResourceAsync(LearningResourceType.Lesson, id, LearningOperation.ViewContent, cancellationToken);

    [Authorize(Roles = AppRoles.Teacher)]
    [HttpGet]
    public Task<IActionResult> ManageCourse(Guid id, CancellationToken cancellationToken) =>
        OpenResourceAsync(LearningResourceType.Course, id, LearningOperation.ManageContent, cancellationToken);

    private async Task<IActionResult> OpenResourceAsync(
        LearningResourceType type, Guid id, LearningOperation operation, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Challenge();

        var access = await learningAccessService.CheckAccessAsync(userId, type, id, operation, cancellationToken);
        return access switch
        {
            LearningAccessResult.Allowed => Dashboard(),
            LearningAccessResult.NotFound => NotFound(),
            _ => Forbid()
        };
    }

    private IActionResult Dashboard()
    {
        ViewData["ActiveSection"] = "courses";
        return View("~/Views/Home/Index.cshtml");
    }
}
