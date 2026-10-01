using System.Security.Claims;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Domain.Authorization;
using EnglishLearningPlatform.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Authorization;

public sealed class ResourceOwnerAuthorizationHandlerTests
{
    private sealed class OwnedResourceStub : IOwnedResource
    {
        public Guid OwnerUserId { get; init; }
    }

    private static ClaimsPrincipal Principal(Guid? userId, bool authenticated = true)
    {
        List<Claim> claims = userId.HasValue
            ? [new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())]
            : [];
        var identity = new ClaimsIdentity(
            claims,
            authenticated ? "TestAuth" : null);
        return new ClaimsPrincipal(identity);
    }

    private static async Task<bool> HandleAsync(ClaimsPrincipal user, IOwnedResource resource)
    {
        var requirement = new ResourceOwnerRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, resource);
        await new ResourceOwnerAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task OwnerUser_Succeeds()
    {
        var ownerId = Guid.NewGuid();
        var resource = new OwnedResourceStub { OwnerUserId = ownerId };

        Assert.True(await HandleAsync(Principal(ownerId), resource));
    }

    [Fact]
    public async Task DifferentUser_Fails()
    {
        var resource = new OwnedResourceStub { OwnerUserId = Guid.NewGuid() };

        Assert.False(await HandleAsync(Principal(Guid.NewGuid()), resource));
    }

    [Fact]
    public async Task UnauthenticatedUser_Fails()
    {
        var ownerId = Guid.NewGuid();
        var resource = new OwnedResourceStub { OwnerUserId = ownerId };

        Assert.False(await HandleAsync(Principal(ownerId, authenticated: false), resource));
    }

    [Fact]
    public async Task NonGuidNameIdentifier_Fails()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "not-a-guid")],
            "TestAuth");
        var user = new ClaimsPrincipal(identity);
        var resource = new OwnedResourceStub { OwnerUserId = Guid.NewGuid() };

        Assert.False(await HandleAsync(user, resource));
    }
}
