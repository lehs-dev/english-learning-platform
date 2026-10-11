using System.Security.Claims;
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
        {
            // Role changes take effect on the next request, even within the
            // security-stamp validation interval. Never trust a stale role cookie.
            var currentRoles = await userManager.GetRolesAsync(user);
            var claimedRoles = context.Principal.FindAll(ClaimTypes.Role).ToList();
            if (currentRoles.Count == 1 && claimedRoles.Count == 1 &&
                AppRoles.All.Contains(currentRoles[0], StringComparer.Ordinal) &&
                string.Equals(currentRoles[0], claimedRoles[0].Value, StringComparison.Ordinal))
                return;
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(
            IdentityConstants.ApplicationScheme);
    }
}
