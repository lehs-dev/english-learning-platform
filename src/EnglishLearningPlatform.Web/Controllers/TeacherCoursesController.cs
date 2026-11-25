using System.Security.Claims;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Web.Infrastructure;
using EnglishLearningPlatform.Web.Models.Courses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EnglishLearningPlatform.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Teacher)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeacherCoursesController(ICourseAuthoringService courses, ITeacherCourseReportService reports,
    IStudentLearningCourseService learning, ITeacherAssessmentDraftService assessments, LessonMediaStore mediaStore,
    ILogger<TeacherCoursesController> logger) : Controller
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private IActionResult Denied(LearningAccessResult access) => access == LearningAccessResult.NotFound ? NotFound() : Forbid();
    [HttpGet]
    public async Task<IActionResult> Students(Guid id, CourseStudentsQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest();
        var result = await reports.GetAsync(UserId, id, query, ct);
        return result.Access == LearningAccessResult.Allowed ? View(result.Value) : Denied(result.Access);
    }
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] TeacherCourseListQuery query, CancellationToken ct)
    {
        var result = await learning.MyCoursesAsync(UserId, true, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        var model = TeacherCourseListModel.Create(result.Value!, query);
        ModelState.Clear();
        return View(model);
    }
    [HttpGet]
    public async Task<IActionResult> Edit(Guid? id, CancellationToken ct)
    {
        if (!id.HasValue)
        {
            var mine = await learning.MyCoursesAsync(UserId, true, ct);
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
            var check = id.HasValue ? (await courses.CourseInputAsync(UserId, id.Value, ct)).Access : (await learning.MyCoursesAsync(UserId, true, ct)).Access;
            return check == LearningAccessResult.Allowed ? View(new CourseEditModel(id, input)) : Denied(check);
        }
        var result = await courses.SaveCourseAsync(UserId, id, input, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        if (!result.Success) { AddErrors(result); return View(new CourseEditModel(id, input)); }
        TempData["Success"] = "Đã lưu khóa học.";
        return RedirectToAction(id.HasValue ? nameof(Edit) : nameof(Manage), new { id = result.Id });
    }
    [HttpGet]
    public async Task<IActionResult> Manage(Guid id, CancellationToken ct, string? tab = null, Guid? moduleId = null, Guid? lessonId = null)
    {
        tab = tab is "assessments" or "publishing" ? tab : "content";
        var page = await learning.OpenCourseAsync(UserId, id, true, ct);
        if (page.Access != LearningAccessResult.Allowed) return Denied(page.Access);

        if (tab == "content") return await RenderContentAsync(page.Value!, moduleId, lessonId, null, ct);

        var model = new AuthoringModel(page.Value!, new Dictionary<Guid, IReadOnlyList<ResourceContent>>()) { Tab = tab };
        if (tab == "assessments")
        {
            var result = await assessments.GetListAsync(UserId, ct, courseId: id);
            if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
            model = model with { Assessments = result.Value! };
        }
        return View(model);
    }
    [HttpGet]
    public async Task<IActionResult> Content(Guid courseId, ContentKind kind, Guid parentId, Guid? id, CancellationToken ct)
    {
        var result = await courses.ContentInputAsync(UserId, courseId, kind, parentId, id, ct);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        var input = result.Value!;
        var mediaUrl = LessonMediaResource.TryParse(input.ResourceUrl, out _) ? input.ResourceUrl : null;
        if (mediaUrl is not null) input.ResourceUrl = null;
        return await RenderContentEditorAsync(new ContentEditModel(courseId, kind, parentId, id, input, mediaUrl), ct);
    }
    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(LessonMediaStore.RequestMaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = LessonMediaStore.RequestMaxBytes)]
    public async Task<IActionResult> Content(Guid courseId, ContentKind kind, Guid parentId, Guid? id,
        [Bind(Prefix = "Input")] ContentInput input, IFormFile? mediaFile, CancellationToken ct)
    {
        var check = await courses.ContentInputAsync(UserId, courseId, kind, parentId, id, ct);
        if (check.Access != LearningAccessResult.Allowed) return Denied(check.Access);

        var existingMedia = LessonMediaResource.TryParse(check.Value?.ResourceUrl, out var media) ? media.Url : null;
        var editor = new ContentEditModel(courseId, kind, parentId, id, input, existingMedia);
        if (kind == ContentKind.Module)
        {
            if (string.IsNullOrWhiteSpace(input.Title))
                ModelState.TryAddModelError("Input.Title", "Vui lòng nhập tên chương học.");
            if (ModelState.TryGetValue("Input.Visibility", out var visibility) && visibility.Errors.Count > 0)
            {
                visibility.Errors.Clear();
                visibility.Errors.Add("Trạng thái hiển thị chương học không hợp lệ.");
            }
        }
        if (kind == ContentKind.Resource &&
            input.ResourceUrl?.Trim().StartsWith(LessonMediaResource.RoutePrefix, StringComparison.OrdinalIgnoreCase) == true)
            ModelState.AddModelError("Input.ResourceUrl", "Dùng trường tải tệp để thêm học liệu.");
        if (!ModelState.IsValid) return await RenderContentEditorAsync(editor, ct);

        string? submittedUrl = input.ResourceUrl;
        LessonMediaResource? newMedia = null;
        if (mediaFile is not null)
        {
            var (limit, help) = (kind, input.ResourceType) switch
            {
                (ContentKind.Resource, LessonResourceType.Image) => (LessonMediaStore.ImageMaxBytes, "Chọn ảnh PNG, JPEG hoặc WebP, tối đa 5 MiB."),
                (ContentKind.Resource, LessonResourceType.Audio) => (LessonMediaStore.AudioMaxBytes, "Chọn âm thanh MP3, WAV hoặc Ogg, tối đa 10 MiB."),
                _ => (0, "Chỉ tải tệp lên khi chọn loại Hình ảnh hoặc Âm thanh.")
            };
            try
            {
                if (mediaFile.Length > 0 && mediaFile.Length <= limit)
                    newMedia = await mediaStore.SaveAsync(mediaFile, input.ResourceType, id ?? Guid.NewGuid(), ct);
                if (!newMedia.HasValue) ModelState.AddModelError(nameof(mediaFile), help);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogError(exception, "Could not store media for lesson {LessonId}.", parentId);
                ModelState.AddModelError(nameof(mediaFile), "Không thể lưu tệp lúc này. Vui lòng thử lại.");
            }
            if (!ModelState.IsValid) return await RenderContentEditorAsync(editor, ct);
            input.ResourceUrl = newMedia!.Value.Url;
        }
        else if (existingMedia is not null && string.IsNullOrWhiteSpace(input.ResourceUrl) && media.Matches(input.ResourceType))
        {
            input.ResourceUrl = existingMedia;
        }
        var result = await courses.SaveContentAsync(UserId, courseId, kind, parentId, id, input, ct);
        if (!result.Success && newMedia is { } unused) mediaStore.Delete(unused);
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        if (!result.Success)
        {
            input.ResourceUrl = submittedUrl;
            AddErrors(result);
            return await RenderContentEditorAsync(editor, ct);
        }
        TempData["Success"] = "Đã lưu nội dung.";
        return kind switch
        {
            ContentKind.Module => RedirectToAction(nameof(Manage), new { id = courseId, moduleId = result.Id }),
            ContentKind.Lesson => RedirectToAction(nameof(Manage), new { id = courseId, moduleId = parentId, lessonId = result.Id }),
            _ => RedirectToAction(nameof(Manage), new { id = courseId, lessonId = parentId })
        };
    }

    private async Task<IActionResult> RenderContentEditorAsync(ContentEditModel editor, CancellationToken ct)
    {
        var page = await learning.OpenCourseAsync(UserId, editor.CourseId, true, ct);
        if (page.Access != LearningAccessResult.Allowed) return Denied(page.Access);
        var moduleId = editor.Kind switch
        {
            ContentKind.Module => editor.Id,
            ContentKind.Lesson => editor.ParentId,
            _ => null
        };
        var lessonId = editor.Kind == ContentKind.Resource ? editor.ParentId
            : editor.Kind == ContentKind.Lesson ? editor.Id : null;
        return await RenderContentAsync(page.Value!, moduleId, lessonId, editor, ct);
    }

    private async Task<IActionResult> RenderContentAsync(CoursePage page, Guid? moduleId, Guid? lessonId,
        ContentEditModel? editor, CancellationToken ct)
    {
        var resources = await courses.AuthoringResourcesAsync(UserId, page.Course.Id, ct);
        if (resources.Access != LearningAccessResult.Allowed) return Denied(resources.Access);
        return View(nameof(Manage), new AuthoringModel(page, resources.Value!)
        {
            ModuleId = moduleId,
            LessonId = lessonId,
            Editor = editor
        });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteResource(Guid courseId, Guid lessonId, Guid id, CancellationToken ct) =>
        Mutation(await courses.DeleteResourceAsync(UserId, courseId, lessonId, id, ct), courseId, lessonId: lessonId);
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reorder(Guid courseId, ContentKind kind, Guid parentId, Guid[] ids,
        CancellationToken ct, Guid? moduleId = null, Guid? lessonId = null) =>
        !ModelState.IsValid ? BadRequest() : Mutation(await courses.ReorderAsync(UserId, courseId, kind, parentId, ids, ct),
            courseId, moduleId: moduleId, lessonId: lessonId);
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) => Mutation(await courses.ChangeStatusAsync(UserId, id, CourseStatus.Published, ct), id, "publishing");
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unpublish(Guid id, CancellationToken ct) => Mutation(await courses.ChangeStatusAsync(UserId, id, CourseStatus.Unpublished, ct), id, "publishing");
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) => Mutation(await courses.ChangeStatusAsync(UserId, id, CourseStatus.Archived, ct), id, "publishing");
    private IActionResult Mutation(WriteOutcome result, Guid id, string? tab = null, Guid? moduleId = null, Guid? lessonId = null)
    {
        if (result.Access != LearningAccessResult.Allowed) return Denied(result.Access);
        TempData[result.Success ? "Success" : "Error"] = result.Success ? "Đã cập nhật khóa học." : string.Join("\n", result.Errors.Values.SelectMany(e => e));
        return RedirectToAction(nameof(Manage), new { id, tab, moduleId, lessonId });
    }
    private void AddErrors(WriteOutcome result)
    {
        foreach (var error in result.Errors) foreach (var message in error.Value) ModelState.AddModelError("Input." + error.Key, message);
    }
}
