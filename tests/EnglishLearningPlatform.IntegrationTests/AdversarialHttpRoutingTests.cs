using System.Net;
using EnglishLearningPlatform.Infrastructure.Identity;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class AdversarialHttpRoutingTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    private static void AssertRedirectMatches(string actualLocation, string expectedPath)
    {
        var isMatch = actualLocation.Equals(expectedPath, StringComparison.OrdinalIgnoreCase) ||
                      actualLocation.Equals(expectedPath + "/Index", StringComparison.OrdinalIgnoreCase) ||
                      (expectedPath.EndsWith("/Index", StringComparison.OrdinalIgnoreCase) &&
                       actualLocation.Equals(expectedPath[..^6], StringComparison.OrdinalIgnoreCase));

        Assert.True(isMatch, $"Expected redirect to '{expectedPath}' but was redirected to '{actualLocation}'.");
    }

    [Theory]
    [InlineData("/Home?debug=true", "/Learning/Dashboard")]
    [InlineData("/HOME", "/Learning/Dashboard")]
    [InlineData("/HOME/INDEX", "/Learning/Dashboard")]
    [InlineData("/home/index/", "/Learning/Dashboard")]
    [InlineData("/#fragment", "/Learning/Dashboard")]
    [InlineData("/?query=123", "/Learning/Dashboard")]
    [InlineData("//evil.com", "/Learning/Dashboard")]
    [InlineData("https://evil.com", "/Learning/Dashboard")]
    [InlineData("javascript:alert(1)", "/Learning/Dashboard")]
    public async Task Login_WithAdversarialReturnUrls_RedirectsToRoleWorkspace(string returnUrl, string expectedWorkspace)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);

        using var response = await IdentityTestHelpers.LoginAsync(client, user, returnUrl: returnUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var location = response.Headers.Location.ToString();
        Assert.False(location.Contains("evil.com", StringComparison.OrdinalIgnoreCase),
            $"Security vulnerability: redirected to external domain for returnUrl: {returnUrl}");

        AssertRedirectMatches(location, expectedWorkspace);
    }

    [Fact]
    public async Task Login_WithAppRelativeReturnUrl_ChecksBehavior()
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);

        using var response = await IdentityTestHelpers.LoginAsync(client, user, returnUrl: "~/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var location = response.Headers.Location.ToString();
        // If "~/" was treated as valid returnUrl, Location is "/".
        // If treated as invalid returnUrl, Location is "/Learning/Dashboard".
        // Either way, if followed, student ends up at "/Learning/Dashboard".
        Assert.True(location == "/" || location.Equals("/Learning/Dashboard", StringComparison.OrdinalIgnoreCase),
            $"Unexpected location '{location}' for returnUrl '~/'");

        // Now follow the redirect if location was "/"
        if (location == "/")
        {
            using var followResponse = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.Redirect, followResponse.StatusCode);
            AssertRedirectMatches(followResponse.Headers.Location!.ToString(), "/Learning/Dashboard");
        }
    }

    [Theory]
    [InlineData("/Courses?category=English", "/Courses?category=English")]
    [InlineData("/HomeSweetHome", "/HomeSweetHome")]
    [InlineData("/Home/About", "/Home/About")]
    public async Task Login_WithValidLocalSubpath_PreservesReturnUrl(string returnUrl, string expectedLocation)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);

        using var response = await IdentityTestHelpers.LoginAsync(client, user, returnUrl: returnUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(expectedLocation, response.Headers.Location.ToString());
    }
}
