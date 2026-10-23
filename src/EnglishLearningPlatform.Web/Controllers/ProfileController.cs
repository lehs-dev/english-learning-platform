using System.Security.Claims;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProfileController(IProfileService profileService, IStudentLearningService learning) : Controller
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
        await PopulateAssessmentResultsAsync(model, userId, ct);
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
        await PopulateAssessmentResultsAsync(model, userId, ct);
        return View(model);
    }

    private async Task PopulateAssessmentResultsAsync(ProfileViewModel model, Guid userId, CancellationToken ct)
    {
        // Profile Teacher/Admin giữ nguyên, không gọi dịch vụ kết quả của Student.
        if (model.Role != "Student")
            return;

        var results = await learning.GetRepresentativeResultsAsync(userId, ct);
        if (results.Access == LearningAccessResult.Allowed)
            model.AssessmentResults = results.Value!;
    }
}
