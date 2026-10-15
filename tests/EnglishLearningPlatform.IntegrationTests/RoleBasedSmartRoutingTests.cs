using System.Net;
using EnglishLearningPlatform.Infrastructure.Identity;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

/// <summary>
/// Opaque-box E2E test suite for Tier 1: Role-Based Smart Routing & Safe ReturnUrl Handling.
/// Covers requirements from ORIGINAL_REQUEST.md § R1 and PROJECT.md Features 1, 2, 3.
/// </summary>
public sealed class RoleBasedSmartRoutingTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
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
    [InlineData(AppRoles.Student, "/Learning/Dashboard")]
    [InlineData(AppRoles.Teacher, "/TeacherCourses")]
    [InlineData(AppRoles.Admin, "/Admin/Transactions")]
    public async Task Login_WithoutReturnUrl_RedirectsToRoleWorkspace(string role, string expectedPath)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        using var client = IdentityTestHelpers.CreateClient(factory);

        using var response = await IdentityTestHelpers.LoginAsync(client, user, returnUrl: null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        AssertRedirectMatches(response.Headers.Location.ToString(), expectedPath);
    }

    [Theory]
    [InlineData(AppRoles.Student, "/Profile")]
    [InlineData(AppRoles.Student, "/Courses")]
    [InlineData(AppRoles.Teacher, "/Profile")]
    [InlineData(AppRoles.Admin, "/Profile")]
    public async Task Login_WithValidLocalReturnUrl_RedirectsToRequestedUrl(string role, string returnUrl)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        using var client = IdentityTestHelpers.CreateClient(factory);

        using var response = await IdentityTestHelpers.LoginAsync(client, user, returnUrl: returnUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(returnUrl, response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData(AppRoles.Student, "https://attacker.example.com/steal", "/Learning/Dashboard")]
    [InlineData(AppRoles.Teacher, "https://attacker.example.com/steal", "/TeacherCourses")]
    [InlineData(AppRoles.Admin, "https://attacker.example.com/steal", "/Admin/Transactions")]
    [InlineData(AppRoles.Student, "//evil.com/phishing", "/Learning/Dashboard")]
    [InlineData(AppRoles.Teacher, "javascript:alert(1)", "/TeacherCourses")]
    [InlineData(AppRoles.Student, "   ", "/Learning/Dashboard")]
    public async Task Login_WithInvalidOrExternalReturnUrl_FallsBackToRoleWorkspace(
        string role, string maliciousUrl, string expectedWorkspace)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        using var client = IdentityTestHelpers.CreateClient(factory);

        using var response = await IdentityTestHelpers.LoginAsync(client, user, returnUrl: maliciousUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var actualLocation = response.Headers.Location.ToString();
        Assert.False(actualLocation.Contains("attacker.example.com", StringComparison.OrdinalIgnoreCase),
            "Security vulnerability: Open redirect allowed external host.");
        Assert.False(actualLocation.Contains("evil.com", StringComparison.OrdinalIgnoreCase),
            "Security vulnerability: Open redirect allowed protocol-relative evil.com.");

        AssertRedirectMatches(actualLocation, expectedWorkspace);
    }

    [Theory]
    [InlineData(AppRoles.Student, "/", "/Learning/Dashboard")]
    [InlineData(AppRoles.Teacher, "/", "/TeacherCourses")]
    [InlineData(AppRoles.Admin, "/", "/Admin/Transactions")]
    [InlineData(AppRoles.Student, "/Home", "/Learning/Dashboard")]
    [InlineData(AppRoles.Student, "/Home/Index", "/Learning/Dashboard")]
    public async Task Login_WithRootReturnUrl_FallsBackToRoleWorkspace(
        string role, string rootUrl, string expectedWorkspace)
    {
        // Per RoleNavigation contract: Root URLs ("/", "/Home", "/Home/Index") must fall back to role workspace
        // to prevent redirect loop / landing bounce for authenticated users.
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        using var client = IdentityTestHelpers.CreateClient(factory);

        using var response = await IdentityTestHelpers.LoginAsync(client, user, returnUrl: rootUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        AssertRedirectMatches(response.Headers.Location.ToString(), expectedWorkspace);
    }

    [Fact]
    public async Task GetRoot_UnauthenticatedGuest_ReturnsSuccessAndLandingPage()
    {
        using var client = IdentityTestHelpers.CreateClient(factory);

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Estudy", content);
    }

    [Theory]
    [InlineData(AppRoles.Student, "/Learning/Dashboard")]
    [InlineData(AppRoles.Teacher, "/TeacherCourses")]
    [InlineData(AppRoles.Admin, "/Admin/Transactions")]
    public async Task GetRoot_AuthenticatedUser_RedirectsToRoleWorkspace(string role, string expectedPath)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        using var client = IdentityTestHelpers.CreateClient(factory);

        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        // Access "/" directly with authenticated session cookie
        using var rootResponse = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, rootResponse.StatusCode);
        Assert.NotNull(rootResponse.Headers.Location);
        AssertRedirectMatches(rootResponse.Headers.Location.ToString(), expectedPath);
    }

    [Theory]
    [InlineData(AppRoles.Student, "/TeacherCourses")]
    [InlineData(AppRoles.Student, "/Admin/Transactions")]
    [InlineData(AppRoles.Teacher, "/Admin/Transactions")]
    public async Task CrossRoleAccess_RestrictedWorkspace_IsDenied(string userRole, string forbiddenPath)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, userRole);
        using var client = IdentityTestHelpers.CreateClient(factory);

        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var forbiddenResponse = await client.GetAsync(forbiddenPath);

        // Must either return 403 Forbidden or redirect (302) to AccessDenied / Login
        if (forbiddenResponse.StatusCode == HttpStatusCode.Redirect)
        {
            var location = forbiddenResponse.Headers.Location?.ToString() ?? string.Empty;
            Assert.True(location.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ||
                        location.Contains("Login", StringComparison.OrdinalIgnoreCase),
                $"Unexpected redirect location for cross-role access: {location}");
        }
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        }
    }
}
