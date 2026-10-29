using System.Security.Claims;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Web.Models.Account;
using EnglishLearningPlatform.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProfileController(IProfileService profileService, IStudentLearningCourseService courses,
    IAssessmentQueryService assessments, IWebHostEnvironment environment) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Challenge();

        var profile = await profileService.GetProfileAsync(userId);
        if (profile is null)
            return Challenge();

        var model = ProfileViewModel.FromProfile(profile);
        await PopulateProfileAsync(model, userId, ct);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index([Bind(nameof(ProfileViewModel.FullName))] ProfileViewModel input, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Challenge();

        var profile = await profileService.GetProfileAsync(userId);
        if (profile is null)
            return Challenge();

        if (ModelState.IsValid)
        {
            var result = await profileService.UpdateFullNameAsync(userId, input.FullName);
            if (result.Succeeded)
            {
                TempData["ProfileSuccessMessage"] = "Đã cập nhật họ và tên của bạn.";
                return RedirectToAction(nameof(Index));
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error);
        }

        var model = ProfileViewModel.FromProfile(profile);
        model.FullName = input.FullName;
        // Form lỗi vẫn hiển thị kết quả gốc từ server, không dùng dữ liệu điểm được POST lên.
        await PopulateProfileAsync(model, userId, ct);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Avatar(IFormFile? image, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            await profileService.GetProfileAsync(userId) is null) return Challenge();
        if (image is null || image.Length == 0 || image.Length > ProfileAvatarStore.MaxBytes)
        {
            TempData["ProfileErrorMessage"] = "Chọn ảnh JPG, PNG hoặc WebP có dung lượng tối đa 2 MB.";
            return RedirectToAction(nameof(Index));
        }

        using var stream = new MemoryStream();
        await image.CopyToAsync(stream, ct);
        var bytes = stream.ToArray();
        if (bytes.Length > ProfileAvatarStore.MaxBytes || ProfileAvatarStore.ImageType(bytes) is null)
        {
            TempData["ProfileErrorMessage"] = "Tệp không hợp lệ. Hãy chọn ảnh JPG, PNG hoặc WebP.";
            return RedirectToAction(nameof(Index));
        }
        var path = ProfileAvatarStore.PathFor(environment, userId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await System.IO.File.WriteAllBytesAsync(temporaryPath, bytes, ct);
            System.IO.File.Move(temporaryPath, path, true);
        }
        finally { if (System.IO.File.Exists(temporaryPath)) System.IO.File.Delete(temporaryPath); }
        TempData["ProfileSuccessMessage"] = "Đã cập nhật ảnh đại diện.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> AvatarImage(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            await profileService.GetProfileAsync(userId) is null) return Challenge();
        var path = ProfileAvatarStore.PathFor(environment, userId);
        if (!System.IO.File.Exists(path)) return NotFound();
        var bytes = await System.IO.File.ReadAllBytesAsync(path, ct);
        var contentType = ProfileAvatarStore.ImageType(bytes);
        if (contentType is null) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(bytes, contentType);
    }

    private async Task PopulateProfileAsync(ProfileViewModel model, Guid userId, CancellationToken ct)
    {
        model.HasAvatar = System.IO.File.Exists(ProfileAvatarStore.PathFor(environment, userId));
        // Profile Teacher/Admin giữ nguyên, không gọi dịch vụ kết quả của Student.
        if (model.Role != "Student")
            return;

        var enrolled = await courses.MyCoursesAsync(userId, false, ct);
        if (enrolled.Access == LearningAccessResult.Allowed)
            model.Courses = enrolled.Value!;
        model.AssessmentHistory = await assessments.GetAttemptHistoryAsync(userId, ct: ct) ?? [];
    }
}
