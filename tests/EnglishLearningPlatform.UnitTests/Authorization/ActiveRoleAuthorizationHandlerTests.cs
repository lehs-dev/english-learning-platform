using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Infrastructure.Authorization;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Authorization;

public sealed class ActiveRoleAuthorizationHandlerTests
{
    private static AuthorizationHandlerContext CreateContext(ClaimsPrincipal user)
    {
        var requirement = new ActiveRoleRequirement();
        return new AuthorizationHandlerContext([requirement], user, null);
    }

    private static ClaimsPrincipal PrincipalWithRoles(bool authenticated, params string[] roles)
    {
        var claims = roles.Select(role => new Claim(ClaimTypes.Role, role));
        var identity = new ClaimsIdentity(
            claims,
            authenticated ? "TestAuth" : null);
        return new ClaimsPrincipal(identity);
    }

    private static async Task<bool> HandleAsync(ClaimsPrincipal user)
    {
        var context = CreateContext(user);
        await new ActiveRoleAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Teacher)]
    [InlineData(AppRoles.Admin)]
    public async Task ExactlyOneActiveRole_Succeeds(string role)
    {
        Assert.True(await HandleAsync(PrincipalWithRoles(true, role)));
    }

    [Fact]
    public async Task UnauthenticatedUser_Fails()
    {
        Assert.False(await HandleAsync(PrincipalWithRoles(false, AppRoles.Student)));
    }

    [Fact]
    public async Task NoRoleClaims_Fails()
    {
        Assert.False(await HandleAsync(PrincipalWithRoles(true)));
    }

    [Fact]
    public async Task TwoActiveRoles_Fails()
    {
        Assert.False(await HandleAsync(
            PrincipalWithRoles(true, AppRoles.Student, AppRoles.Teacher)));
    }

    [Fact]
    public async Task UnknownRoleOnly_Fails()
    {
        Assert.False(await HandleAsync(PrincipalWithRoles(true, "Guest")));
    }

    [Fact]
    public async Task DuplicatedSameRoleClaim_Fails()
    {
        Assert.False(await HandleAsync(
            PrincipalWithRoles(true, AppRoles.Teacher, AppRoles.Teacher)));
    }

    [Fact]
    public async Task KnownPlusUnknownRole_Fails()
    {
        Assert.False(await HandleAsync(
            PrincipalWithRoles(true, AppRoles.Student, "Guest")));
    }
}
