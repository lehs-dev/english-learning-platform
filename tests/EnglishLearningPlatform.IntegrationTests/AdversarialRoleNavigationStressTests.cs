using System.Net;
using System.Security.Claims;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Web.Controllers;
using EnglishLearningPlatform.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class AdversarialRoleNavigationStressTests
{
    private static IUrlHelper CreateTestUrlHelper()
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new UrlHelper(actionContext);
    }

    private sealed class DummyController : Controller { }

    #region 1. Root & Home Aliases Boundary Tests (Must Return False)

    [Theory]
    [InlineData("/")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData(null)]
    [InlineData("/Home")]
    [InlineData("/home")]
    [InlineData("/HOME")]
    [InlineData("/HoMe")]
    [InlineData("/Home/")]
    [InlineData("/home/")]
    [InlineData("/HOME/")]
    [InlineData("/Home/Index")]
    [InlineData("/home/index")]
    [InlineData("/HOME/INDEX")]
    [InlineData("/Home/Index/")]
    [InlineData("/home/index/")]
    [InlineData("/HOME/INDEX/")]
    [InlineData("/Home/Index//")]
    [InlineData("/Home/Index///")]
    [InlineData("/?")]
    [InlineData("/#")]
    [InlineData("/?test=1")]
    [InlineData("/#fragment")]
    [InlineData("/?test=1#fragment")]
    [InlineData("/Home?debug=true")]
    [InlineData("/home?debug=true")]
    [InlineData("/HOME?debug=true")]
    [InlineData("/Home#section")]
    [InlineData("/Home/#section")]
    [InlineData("/Home/Index?page=1")]
    [InlineData("/home/index?page=1")]
    [InlineData("/HOME/INDEX?page=1")]
    [InlineData("/Home/Index#heading")]
    [InlineData("/Home/Index/?page=1#heading")]
    public void IsValidReturnUrl_RootAndHomeAliases_ReturnsFalse(string? returnUrl)
    {
        var urlHelper = CreateTestUrlHelper();
        var isValid = RoleNavigation.IsValidReturnUrl(urlHelper, returnUrl);
        Assert.False(isValid, $"Expected returnUrl '{returnUrl}' to be invalid (treated as root/home alias).");
    }

    #endregion

    #region 2. Malicious Schemes & Open Redirect Tests (Must Return False)

    [Theory]
    [InlineData("https://evil.com")]
    [InlineData("http://evil.com")]
    [InlineData("http://evil.com/phish")]
    [InlineData("//evil.com")]
    [InlineData("//evil.com/test")]
    [InlineData("///evil.com")]
    [InlineData("////evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("/\\evil.com/test")]
    [InlineData("/\\\\evil.com")]
    [InlineData("\\evil.com")]
    [InlineData("\\\\evil.com")]
    [InlineData("//")]
    [InlineData("///")]
    [InlineData("/\\")]
    [InlineData("javascript:alert(1)")]
    [InlineData("javascript://alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("mailto:victim@example.com")]
    [InlineData("ftp://files.example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("//localhost")]
    [InlineData("http://localhost:5000")]
    [InlineData("https://localhost:5001")]
    [InlineData("https:evil.com")]
    [InlineData("http:evil.com")]
    [InlineData("https://attacker.example.com/steal")]
    public void IsValidReturnUrl_MaliciousAndExternalUrls_ReturnsFalse(string maliciousUrl)
    {
        var urlHelper = CreateTestUrlHelper();
        var isValid = RoleNavigation.IsValidReturnUrl(urlHelper, maliciousUrl);
        Assert.False(isValid, $"Security failure: malicious/external URL '{maliciousUrl}' was accepted as valid.");
    }

    #endregion

    #region 3. Valid Local URLs & Non-Root Subpaths (Must Return True)

    [Theory]
    [InlineData("/Learning/Dashboard")]
    [InlineData("/learning/dashboard")]
    [InlineData("/TeacherCourses")]
    [InlineData("/TeacherCourses/Index")]
    [InlineData("/Admin/Transactions")]
    [InlineData("/Profile")]
    [InlineData("/Courses")]
    [InlineData("/Courses/Details/42")]
    [InlineData("/Courses?category=English")]
    [InlineData("/Courses/Details/42?ref=search#curriculum")]
    [InlineData("/Practice/Start/1")]
    [InlineData("/Assessment/Take/1")]
    [InlineData("/Checkout/Order/99")]
    [InlineData("/Home/About")]
    [InlineData("/Home/Privacy")]
    [InlineData("/HomeSweetHome")]
    [InlineData("/Homework")]
    [InlineData("/Homepage")]
    [InlineData("/HomeIndex")]
    [InlineData("/Home/Indexed")]
    [InlineData("/Home/IndexPage")]
    [InlineData("/Home/Index2")]
    [InlineData("/Course/Home")]
    public void IsValidReturnUrl_ValidLocalReturnUrls_ReturnsTrue(string validLocalUrl)
    {
        var urlHelper = CreateTestUrlHelper();
        var isValid = RoleNavigation.IsValidReturnUrl(urlHelper, validLocalUrl);
        Assert.True(isValid, $"Expected valid local URL '{validLocalUrl}' to be accepted as valid returnUrl.");
    }

    #endregion

    #region 4. Workspace URL Mapping & Predicates

    [Theory]
    [InlineData(AppRoles.Student, "/Learning/Dashboard")]
    [InlineData(AppRoles.Teacher, "/TeacherCourses")]
    [InlineData(AppRoles.Admin, "/Admin/Transactions")]
    [InlineData("Guest", "/")]
    [InlineData("SuperAdmin", "/")]
    [InlineData("", "/")]
    [InlineData("   ", "/")]
    [InlineData(null, "/")]
    public void GetWorkspaceUrl_MapsCorrectly(string? role, string expectedUrl)
    {
        var actual = RoleNavigation.GetWorkspaceUrl(role);
        Assert.Equal(expectedUrl, actual);
    }

    [Theory]
    [InlineData(AppRoles.Student, true)]
    [InlineData(AppRoles.Teacher, true)]
    [InlineData(AppRoles.Admin, true)]
    [InlineData("Guest", false)]
    [InlineData("SuperUser", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void HasRoleWorkspace_IdentifiesKnownRoles(string? role, bool expected)
    {
        var actual = RoleNavigation.HasRoleWorkspace(role);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(AppRoles.Student, "Dashboard", "Learning")]
    [InlineData(AppRoles.Teacher, "Index", "TeacherCourses")]
    [InlineData(AppRoles.Admin, "Transactions", "Admin")]
    [InlineData("Unknown", "Index", "Home")]
    [InlineData(null, "Index", "Home")]
    public void RedirectToRoleWorkspace_ReturnsCorrectActionAndController(
        string? role, string expectedAction, string expectedController)
    {
        var controller = new DummyController();
        var result = controller.RedirectToRoleWorkspace(role);

        var redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(expectedAction, redirectResult.ActionName);
        Assert.Equal(expectedController, redirectResult.ControllerName);
    }

    #endregion

    #region 5. Edge Case: App-Relative URLs (~/)

    [Theory]
    [InlineData("~/")]
    [InlineData("~/Home")]
    [InlineData("~/Home/Index")]
    public void IsValidReturnUrl_AppRelativeRootUrls_IsLocalAndSafe(string appRelativeUrl)
    {
        var urlHelper = CreateTestUrlHelper();
        // ASP.NET Core UrlHelper considers "~/..." as local virtual paths
        Assert.True(urlHelper.IsLocalUrl(appRelativeUrl));

        var isValid = RoleNavigation.IsValidReturnUrl(urlHelper, appRelativeUrl);
        // Note: RoleNavigation currently evaluates pathOnly as "~" or "~/Home", which passes IsValidReturnUrl.
        // At runtime, LocalRedirect("~/") redirects to "/", where HomeController.Index safely redirects to role workspace.
        Assert.True(isValid);
    }

    #endregion
}
