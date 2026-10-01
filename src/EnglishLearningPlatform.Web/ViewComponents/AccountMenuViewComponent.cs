using System.Security.Claims;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Web.Models.Account;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.ViewComponents;

public sealed class AccountMenuViewComponent(IProfileService profileService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        ProfileViewModel? model = null;
        if (User.Identity?.IsAuthenticated == true
            && Guid.TryParse(HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            var profile = await profileService.GetProfileAsync(userId);
            if (profile is not null)
                model = ProfileViewModel.FromProfile(profile);
        }

        return View(model);
    }
}
