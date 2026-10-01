using System.Security.Claims;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Controllers;

[Authorize]
public sealed class ProfileController(IProfileService profileService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Challenge();

        var profile = await profileService.GetProfileAsync(userId);
        return profile is null ? Challenge() : View(ProfileViewModel.FromProfile(profile));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index([Bind(nameof(ProfileViewModel.FullName))] ProfileViewModel input)
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
        return View(model);
    }
}
