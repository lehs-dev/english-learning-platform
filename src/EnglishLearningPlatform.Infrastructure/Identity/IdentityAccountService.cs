using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Identity;

public sealed class IdentityAccountService(
    UserManager<ApplicationUser> userManager,
    AppDbContext dbContext) : IAccountService
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
                createResult.Errors.Select(error => error.Description));
        }

        var roleResult = await userManager.AddToRoleAsync(user, AppRoles.Student);

        if (!roleResult.Succeeded)
        {
            await transaction.RollbackAsync();

            return RegisterStudentResult.Failure(
                roleResult.Errors.Select(error => error.Description));
        }

        await transaction.CommitAsync();

        return RegisterStudentResult.Success();
    }
}
