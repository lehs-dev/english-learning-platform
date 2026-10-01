using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishLearningPlatform.Infrastructure.Authorization;

/// <summary>
/// Đăng ký các authorization policy của nền tảng và các handler đi kèm.
/// Gọi trong <c>AddInfrastructure</c>; không cần thay đổi Program.cs.
/// </summary>
public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.SingleActiveRole, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ActiveRoleRequirement());
            })
            .AddPolicy(AuthorizationPolicies.Student, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(AppRoles.Student);
                policy.AddRequirements(new ActiveRoleRequirement());
            })
            .AddPolicy(AuthorizationPolicies.Teacher, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(AppRoles.Teacher);
                policy.AddRequirements(new ActiveRoleRequirement());
            })
            .AddPolicy(AuthorizationPolicies.Admin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(AppRoles.Admin);
                policy.AddRequirements(new ActiveRoleRequirement());
            });

        services.AddScoped<IAuthorizationHandler, ActiveRoleAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, ResourceOwnerAuthorizationHandler>();

        return services;
    }
}
