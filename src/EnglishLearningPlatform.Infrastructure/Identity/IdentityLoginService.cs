using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace EnglishLearningPlatform.Infrastructure.Identity;

public sealed class IdentityLoginService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : ILoginService
{
    public async Task<LoginResult> LoginAsync(LoginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await userManager.FindByEmailAsync(request.Email.Trim());

        if (user is null)
            return new LoginResult(LoginStatus.InvalidCredentials);

        if (user.AccountStatus != AccountStatus.Active)
            return new LoginResult(LoginStatus.AccountUnavailable);

        var result = await signInManager.PasswordSignInAsync(
            user,
            request.Password,
            request.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
            return new LoginResult(LoginStatus.Succeeded);

        if (result.IsLockedOut)
            return new LoginResult(LoginStatus.TemporarilyLocked);

        if (result.IsNotAllowed || result.RequiresTwoFactor)
            return new LoginResult(LoginStatus.AccountUnavailable);

        return new LoginResult(LoginStatus.InvalidCredentials);
    }
}
