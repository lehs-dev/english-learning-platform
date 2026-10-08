using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EnglishLearningPlatform.Web.Controllers;
[Authorize(Roles = AppRoles.Student + "," + AppRoles.Teacher, Policy = AuthorizationPolicies.SingleActiveRole)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LearningController(ICourseService courses) : Controller
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private IActionResult Denied(LearningAccessResult access) => access == LearningAccessResult.NotFound ? NotFound() : Forbid();
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (User.IsInRole(AppRoles.Teacher)) return RedirectToAction("Index", "TeacherCourses");
        var result = await courses.MyCoursesAsync(UserId, false, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Denied(result.Access);
    }
    [HttpGet]
    public async Task<IActionResult> Course(Guid id, CancellationToken ct)
    {
        var result = await courses.OpenCourseAsync(UserId, id, false, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Denied(result.Access);
    }
    [HttpGet]
    public async Task<IActionResult> Module(Guid id, CancellationToken ct)
    {
        var result = await courses.OpenModuleAsync(UserId, id, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Denied(result.Access);
    }
    [HttpGet]
    public async Task<IActionResult> Lesson(Guid id, CancellationToken ct)
    {
        var result = await courses.OpenLessonAsync(UserId, id, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Denied(result.Access);
    }
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AuthorizationPolicies.Student)]
    public async Task<IActionResult> Completion(Guid id, bool? completed, CancellationToken ct)
    {
        if (!ModelState.IsValid || !completed.HasValue) return BadRequest();
        var result = await courses.SetLessonCompletedAsync(UserId, id, completed.Value, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        TempData["Success"] = completed.Value ? "Đã đánh dấu bài học hoàn thành." : "Đã đánh dấu bài học chưa hoàn thành.";
        return RedirectToAction(nameof(Lesson), new { id });
    }
    [HttpGet]
    public async Task<IActionResult> Resource(Guid id, CancellationToken ct)
    {
        var result = await courses.OpenResourceAsync(UserId, id, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Denied(result.Access);
    }
    [Authorize(Policy = AuthorizationPolicies.Teacher), HttpGet]
    public async Task<IActionResult> ManageCourse(Guid id, CancellationToken ct)
    {
        var result = await courses.OpenCourseAsync(UserId, id, true, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        var resources = await courses.AuthoringResourcesAsync(UserId, id, ct);
        return resources.Access == LearningAccessResult.Allowed
            ? View("~/Views/TeacherCourses/Manage.cshtml", new EnglishLearningPlatform.Web.Models.Courses.AuthoringModel(result.Value!, resources.Value!))
            : Denied(resources.Access);
    }
}
