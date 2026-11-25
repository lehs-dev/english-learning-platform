using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Identity;

public sealed class IdentityRegistrationService(
    UserManager<ApplicationUser> userManager,
    AppDbContext dbContext) : IRegistrationService
{
    public async Task<RegisterStudentResult> RegisterStudentAsync(
        RegisterStudentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email.Trim();

        var user = new ApplicationUser
        {
            FullName = request.FullName.Trim(),
            Email = email,
            UserName = email,
            AccountStatus = AccountStatus.Active
        };

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        var createResult = await userManager.CreateAsync(user, request.Password);

        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync();

            return RegisterStudentResult.Failure(
                createResult.Errors.Select(GetRegistrationMessage).Distinct());
        }

        var roleResult = await userManager.AddToRoleAsync(user, AppRoles.Student);

        if (!roleResult.Succeeded)
        {
            await transaction.RollbackAsync();

            return RegisterStudentResult.Failure(["Không thể tạo tài khoản. Vui lòng thử lại."]);
        }

        await transaction.CommitAsync();

        return RegisterStudentResult.Success();
    }

    private static string GetRegistrationMessage(IdentityError error) => error.Code switch
    {
        nameof(IdentityErrorDescriber.DuplicateUserName) or nameof(IdentityErrorDescriber.DuplicateEmail)
            => "Địa chỉ thư điện tử đã được sử dụng.",
        nameof(IdentityErrorDescriber.InvalidUserName) or nameof(IdentityErrorDescriber.InvalidEmail)
            => "Địa chỉ thư điện tử không hợp lệ.",
        nameof(IdentityErrorDescriber.PasswordTooShort)
            => "Mật khẩu phải có ít nhất 8 ký tự.",
        nameof(IdentityErrorDescriber.PasswordRequiresDigit)
            => "Mật khẩu cần có chữ số.",
        nameof(IdentityErrorDescriber.PasswordRequiresLower)
            => "Mật khẩu cần có chữ thường.",
        nameof(IdentityErrorDescriber.PasswordRequiresUpper)
            => "Mật khẩu cần có chữ hoa.",
        nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric)
            => "Mật khẩu cần có ký tự đặc biệt.",
        _ => "Không thể tạo tài khoản. Vui lòng thử lại."
    };
}
