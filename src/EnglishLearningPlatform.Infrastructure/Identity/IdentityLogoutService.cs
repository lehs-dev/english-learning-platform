using System;
using EnglishLearningPlatform.Application.Identity;
using Microsoft.AspNetCore.Identity;

namespace EnglishLearningPlatform.Infrastructure.Identity;

public sealed class IdentityLogoutService(SignInManager<ApplicationUser> signInManager) : ILogoutService
{
    public Task LogoutAsync()
    {
        return signInManager.SignOutAsync();
    }
}
