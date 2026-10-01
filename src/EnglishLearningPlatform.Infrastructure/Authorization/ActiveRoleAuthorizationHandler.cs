using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;

namespace EnglishLearningPlatform.Infrastructure.Authorization;

/// <summary>
/// Thực thi <see cref="ActiveRoleRequirement"/>: chỉ succeed khi principal đã
/// đăng nhập và có đúng một role claim duy nhất, role đó thuộc
/// <see cref="AppRoles.All"/>. Mọi trường hợp khác (nhiều role, role lạ,
/// claim trùng lặp) đều bị từ chối theo nguyên tắc fail-closed.
/// </summary>
public sealed class ActiveRoleAuthorizationHandler : AuthorizationHandler<ActiveRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveRoleRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var roles = context.User
                .FindAll(ClaimTypes.Role)
                .Select(claim => claim.Value)
                .ToList();

            if (roles.Count == 1 && AppRoles.All.Contains(roles[0], StringComparer.Ordinal))
                context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
