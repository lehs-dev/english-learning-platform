using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace EnglishLearningPlatform.Infrastructure.Identity;

public sealed class IdentityProfileService(UserManager<ApplicationUser> userManager)
    : IProfileService
{
    public async Task<UserProfile?> GetProfileAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.AccountStatus != AccountStatus.Active)
            return null;

        var roles = await userManager.GetRolesAsync(user);
        return new UserProfile(
            user.Id, user.FullName, user.Email ?? string.Empty,
            roles.SingleOrDefault() ?? string.Empty, user.AccountStatus, user.CreatedAtUtc);
    }

    public async Task<UpdateProfileResult> UpdateFullNameAsync(Guid userId, string fullName)
    {
        var name = fullName?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > 200)
            return UpdateProfileResult.Failure("Họ và tên phải có từ 1 đến 200 ký tự.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.AccountStatus != AccountStatus.Active)
            return UpdateProfileResult.Failure("Tài khoản không còn khả dụng.");

        // Chỉ cập nhật họ tên của chính user; email, role và trạng thái không nhận từ form.
        user.FullName = name;
        user.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded
            ? UpdateProfileResult.Success()
            : UpdateProfileResult.Failure(result.Errors.Select(error => error.Description).ToArray());
    }
}
