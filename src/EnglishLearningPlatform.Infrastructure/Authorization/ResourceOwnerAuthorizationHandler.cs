using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace EnglishLearningPlatform.Infrastructure.Authorization;

/// <summary>
/// Thực thi <see cref="ResourceOwnerRequirement"/>: chỉ succeed khi người dùng
/// đã đăng nhập và <see cref="ClaimTypes.NameIdentifier"/> của họ trùng với
/// <see cref="IOwnedResource.OwnerUserId"/> của tài nguyên.
/// Quy tắc "Admin không sửa nội dung học tập" được thực thi ở tầng endpoint:
/// các endpoint nội dung không cấp policy cho Admin.
/// </summary>
public sealed class ResourceOwnerAuthorizationHandler
    : AuthorizationHandler<ResourceOwnerRequirement, IOwnedResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceOwnerRequirement requirement,
        IOwnedResource resource)
    {
        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(userIdValue, out var userId)
            && userId == resource.OwnerUserId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
