using System.Net;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class ProfileAndLoginTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData(AppRoles.Student, "Học viên")]
    [InlineData(AppRoles.Teacher, "Giáo viên")]
    [InlineData(AppRoles.Admin, "Quản trị viên")]
    public async Task EachRole_CanLogin_ViewOwnProfile_AndLogout(string role, string roleLabel)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var dashboard = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        Assert.Contains("data-account-menu", dashboard);
        Assert.Contains("Xem profile", dashboard);
        Assert.Contains(user.FullName, dashboard);

        var profile = WebUtility.HtmlDecode(await client.GetStringAsync("/Profile"));
        Assert.Contains(user.Email!, profile);
        Assert.Contains(roleLabel, profile);

        var token = await IdentityTestHelpers.GetTokenAsync(client, "/");
        using var logout = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        using var afterLogout = await client.GetAsync("/Profile");
        Assert.Equal(HttpStatusCode.Redirect, afterLogout.StatusCode);
        Assert.Contains("/Account/Login", afterLogout.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Register_AlwaysCreatesStudent_EvenWhenClientPostsTeacherRole()
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        var token = await IdentityTestHelpers.GetTokenAsync(client, "/Account/Register");
        var email = $"register-{Guid.NewGuid():N}@example.test";
        using var response = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["FullName"] = "Học viên mới", ["Email"] = email,
                ["Password"] = IdentityTestHelpers.Password, ["ConfirmPassword"] = IdentityTestHelpers.Password,
                ["Role"] = AppRoles.Teacher, ["__RequestVerificationToken"] = token
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.Equal(new[] { AppRoles.Student }, await manager.GetRolesAsync(user));
    }

    [Fact]
    public async Task UpdateProfile_OnlyChangesOwnFullName_IgnoresSensitiveFields()
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        var token = await IdentityTestHelpers.GetTokenAsync(client, "/Profile");
        using var response = await client.PostAsync("/Profile", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "  Nguyễn Khang  ", ["UserId"] = other.Id.ToString(),
            ["Email"] = "changed@example.test", ["Role"] = AppRoles.Admin,
            ["AccountStatus"] = "Disabled", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var updated = await manager.FindByIdAsync(user.Id.ToString());
        Assert.Equal("Nguyễn Khang", updated!.FullName);
        Assert.Equal(user.Email, updated.Email);
        Assert.Equal(AccountStatus.Active, updated.AccountStatus);
        Assert.Equal(new[] { AppRoles.Student }, await manager.GetRolesAsync(updated));
        Assert.Equal(other.FullName, (await manager.FindByIdAsync(other.Id.ToString()))!.FullName);
        Assert.Contains("Nguyễn Khang", WebUtility.HtmlDecode(await client.GetStringAsync("/")));
    }

    [Fact]
    public async Task ProfileUpdate_WithoutAntiforgeryToken_IsRejected()
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        using var response = await client.PostAsync("/Profile", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["FullName"] = "Không được lưu" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(AccountStatus.Locked)]
    [InlineData(AccountStatus.Disabled)]
    public async Task InactiveAccount_CannotLogin_AndExistingSessionIsRejected(AccountStatus status)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var account = (await manager.FindByIdAsync(user.Id.ToString()))!;
            account.AccountStatus = status;
            Assert.True((await manager.UpdateAsync(account)).Succeeded);
        }
        using var profile = await client.GetAsync("/Profile");
        Assert.Equal(HttpStatusCode.Redirect, profile.StatusCode);
        using var deniedLogin = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.OK, deniedLogin.StatusCode);
        Assert.Contains("Không thể đăng nhập", WebUtility.HtmlDecode(await deniedLogin.Content.ReadAsStringAsync()));
    }

    [Theory]
    [InlineData("/Profile", "/Profile")]
    [InlineData("https://example.test/steal", "/")]
    public async Task Login_ReturnUrl_AllowsOnlyLocalUrls(string returnUrl, string expected)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user, returnUrl);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(expected, login.Headers.Location!.ToString());
    }

    [Fact]
    public async Task InvalidCredentials_RenderValidation_WithoutLoggingIn()
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user, password: "Wrong_123!");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("Không thể đăng nhập", WebUtility.HtmlDecode(await login.Content.ReadAsStringAsync()));
        using var profile = await client.GetAsync("/Profile");
        Assert.Equal(HttpStatusCode.Redirect, profile.StatusCode);
    }
}
