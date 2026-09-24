using EnglishLearningPlatform.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace EnglishLearningPlatform.Infrastructure.Identity;

public sealed class AccountStatusCookieEvents(
    UserManager<ApplicationUser> userManager) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(
        CookieValidatePrincipalContext context)
    {
        // Giữ bước kiểm tra security stamp mặc định của Identity.
        await SecurityStampValidator.ValidatePrincipalAsync(context);

        if (context.Principal is null)
            return;

        var user = await userManager.GetUserAsync(context.Principal);

        if (user is not null && user.AccountStatus == AccountStatus.Active)
            return;

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(
            IdentityConstants.ApplicationScheme);
    }
}
