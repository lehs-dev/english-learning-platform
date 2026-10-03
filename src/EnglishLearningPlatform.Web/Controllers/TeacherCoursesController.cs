using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Web.Models.Courses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EnglishLearningPlatform.Web.Controllers;
[Authorize(Policy = AuthorizationPolicies.Teacher)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherCoursesController(ICourseService courses) : Controller
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private IActionResult Denied(LearningAccessResult access) => access == LearningAccessResult.NotFound ? NotFound() : Forbid();
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var result = await courses.MyCoursesAsync(UserId, true, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Denied(result.Access);
    }
    [HttpGet]
    public async Task<IActionResult> Edit(Guid? id, CancellationToken ct)
    {
        if (!id.HasValue)
        {
            var mine = await courses.MyCoursesAsync(UserId, true, ct);
            return mine.Access == LearningAccessResult.Allowed ? View(new CourseEditModel(null, new())) : Denied(mine.Access);
        }
        var result = await courses.CourseInputAsync(UserId, id.Value, ct);
        return result.Access == LearningAccessResult.Allowed ? View(new CourseEditModel(id, result.Value!)) : Denied(result.Access);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid? id, [Bind(Prefix = "Input")] CourseInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var check = id.HasValue ? (await courses.CourseInputAsync(UserId, id.Value, ct)).Access : (await courses.MyCoursesAsync(UserId, true, ct)).Access;
            return check == LearningAccessResult.Allowed ? View(new CourseEditModel(id, input)) : Denied(check);
        }
        var result = await courses.SaveCourseAsync(UserId, id, input, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        if (!result.Success) { AddErrors(result); return View(new CourseEditModel(id, input)); }
        TempData["Success"] = "Đã lưu khóa học.";
        return RedirectToAction(nameof(Manage), new { id = result.Id });
    }
    [HttpGet]
    public async Task<IActionResult> Manage(Guid id, CancellationToken ct)
    {
        var page = await courses.OpenCourseAsync(UserId, id, true, ct);
        if (page.Access != LearningAccessResult.Allowed) return Denied(page.Access);
        var resources = await courses.AuthoringResourcesAsync(UserId, id, ct);
        if (resources.Access != LearningAccessResult.Allowed) return Denied(resources.Access);
        return View(new AuthoringModel(page.Value!, resources.Value!));
    }
    [HttpGet]
    public async Task<IActionResult> Content(Guid courseId, ContentKind kind, Guid parentId, Guid? id, CancellationToken ct)
    {
        var result = await courses.ContentInputAsync(UserId, courseId, kind, parentId, id, ct);
        return result.Access == LearningAccessResult.Allowed ? View(new ContentEditModel(courseId, kind, parentId, id, result.Value!)) : Denied(result.Access);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Content(Guid courseId, ContentKind kind, Guid parentId, Guid? id,
        [Bind(Prefix = "Input")] ContentInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var check = await courses.ContentInputAsync(UserId, courseId, kind, parentId, id, ct);
            return check.Access == LearningAccessResult.Allowed ? View(new ContentEditModel(courseId, kind, parentId, id, input)) : Denied(check.Access);
        }
        var result = await courses.SaveContentAsync(UserId, courseId, kind, parentId, id, input, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        if (!result.Success) { AddErrors(result); return View(new ContentEditModel(courseId, kind, parentId, id, input)); }
        TempData["Success"] = "Đã lưu nội dung.";
        return RedirectToAction(nameof(Manage), new { id = courseId });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteResource(Guid courseId, Guid lessonId, Guid id, CancellationToken ct) =>
        Mutation(await courses.DeleteResourceAsync(UserId, courseId, lessonId, id, ct), courseId);
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reorder(Guid courseId, ContentKind kind, Guid parentId, Guid[] ids, CancellationToken ct) =>
        !ModelState.IsValid ? BadRequest() : Mutation(await courses.ReorderAsync(UserId, courseId, kind, parentId, ids, ct), courseId);
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) => Mutation(await courses.ChangeStatusAsync(UserId, id, CourseStatus.Published, ct), id);
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unpublish(Guid id, CancellationToken ct) => Mutation(await courses.ChangeStatusAsync(UserId, id, CourseStatus.Unpublished, ct), id);
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) => Mutation(await courses.ChangeStatusAsync(UserId, id, CourseStatus.Archived, ct), id);
    private IActionResult Mutation(WriteOutcome result, Guid id)
    {
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        TempData[result.Success ? "Success" : "Error"] = result.Success ? "Đã cập nhật khóa học." : string.Join("\n", result.Errors.Values.SelectMany(e => e));
        return RedirectToAction(nameof(Manage), new { id });
    }
    private void AddErrors(WriteOutcome result)
    {
        foreach (var error in result.Errors) foreach (var message in error.Value) ModelState.AddModelError("Input." + error.Key, message);
    }
}
