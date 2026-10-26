using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
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

        var expectedWorkspace = role switch
        {
            AppRoles.Student => "/Learning/Dashboard",
            AppRoles.Teacher => "/TeacherCourses",
            AppRoles.Admin => "/Admin/Transactions",
            _ => "/"
        };
        Assert.Equal(expectedWorkspace, login.Headers.Location!.ToString());

        using var rootRedirect = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, rootRedirect.StatusCode);
        Assert.Equal(expectedWorkspace, rootRedirect.Headers.Location!.ToString());

        var dashboard = WebUtility.HtmlDecode(await client.GetStringAsync(expectedWorkspace));
        Assert.Contains("data-account-menu", dashboard);
        var accountMenu = Regex.Match(dashboard, "<details\\b[^>]*data-account-menu[^>]*>.*?</details>",
            RegexOptions.Singleline);
        Assert.True(accountMenu.Success);
        Assert.Contains("href=\"/Profile\"", accountMenu.Value);
        Assert.Contains("Hồ sơ cá nhân", accountMenu.Value);
        Assert.Contains(user.FullName, dashboard);

        var profile = WebUtility.HtmlDecode(await client.GetStringAsync("/Profile"));
        Assert.Contains(user.Email!, profile);
        Assert.Contains(roleLabel, profile);
        if (role == AppRoles.Student)
            Assert.Contains("id=\"assessment-results\"", profile);
        else
            Assert.DoesNotContain("id=\"assessment-results\"", profile);

        var token = await IdentityTestHelpers.GetTokenAsync(client, expectedWorkspace);
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
    public async Task Register_PreservesLocalReturnUrl_AndShowsLoginSuccessNotice()
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        const string returnUrl = "/Practice";
        var initialLoginHtml = WebUtility.HtmlDecode(await client.GetStringAsync(
            $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}"));
        var registerLink = Regex.Match(initialLoginHtml, "href=\"([^\"]+)\"[^>]*>Đăng ký ngay");
        Assert.True(registerLink.Success);
        string getPath = registerLink.Groups[1].Value;
        string registerHtml = WebUtility.HtmlDecode(await client.GetStringAsync(getPath));
        Assert.Contains("name=\"ReturnUrl\"", registerHtml);
        Assert.Contains($"value=\"{returnUrl}\"", registerHtml);
        Assert.Contains("returnUrl=%2FPractice", registerHtml);

        var token = await IdentityTestHelpers.GetTokenAsync(client, getPath);
        var email = $"return-url-{Guid.NewGuid():N}@example.test";
        using var response = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["FullName"] = "Học viên quay lại", ["Email"] = email,
                ["Password"] = IdentityTestHelpers.Password, ["ConfirmPassword"] = IdentityTestHelpers.Password,
                ["ReturnUrl"] = returnUrl, ["__RequestVerificationToken"] = token
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Contains("/Account/Login?returnUrl=%2FPractice", location.ToString());

        using var loginPage = await client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        string loginHtml = WebUtility.HtmlDecode(await loginPage.Content.ReadAsStringAsync());
        Assert.Contains("Đăng ký thành công. Vui lòng đăng nhập.", loginHtml);
        Assert.Contains("name=\"ReturnUrl\"", loginHtml);
        Assert.Contains($"value=\"{returnUrl}\"", loginHtml);

        using var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Email"] = email, ["Password"] = IdentityTestHelpers.Password, ["ReturnUrl"] = returnUrl,
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(loginHtml,
                    "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value)
            }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(returnUrl, login.Headers.Location!.ToString());

        // ReturnUrl ngoài site không được đưa vào form đăng ký.
        string unsafeHtml = WebUtility.HtmlDecode(await client.GetStringAsync(
            "/Account/Register?returnUrl=" + Uri.EscapeDataString("https://example.test/steal")));
        Assert.DoesNotContain("example.test/steal", unsafeHtml);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Login_DemoPassword_RoundTripsSpecialCharacters_OnlyInDevelopment(string environment)
    {
        const string password = "Demo_&\"'\\</script><script>alert('test')</script>\r\n123!";
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LearningDemo:Enabled"] = "false", ["LearningDemo:Password"] = password,
                ["Database:ApplyMigrationsOnStartup"] = "false"
            }));
        });
        using var client = IdentityTestHelpers.CreateClient(app);
        var html = await client.GetStringAsync("/Account/Login");
        var attribute = Regex.Match(html, "data-demo-password=\"([^\"]*)\"");
        if (environment == "Development")
        {
            Assert.True(attribute.Success);
            Assert.DoesNotContain("</script>", attribute.Groups[1].Value);
            Assert.Equal(password, WebUtility.HtmlDecode(attribute.Groups[1].Value));
        }
        else
        {
            Assert.True(!attribute.Success || string.IsNullOrEmpty(attribute.Groups[1].Value));
            Assert.DoesNotContain("Demo_", html);
        }
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    public async Task ModalAccountForm_RendersPanelWithAntiforgery_AndPreservesOnlyLocalReturnUrl(string path)
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await SendModalAsync(client, HttpMethod.Get,
            path + "?returnUrl=" + Uri.EscapeDataString("/Practice?skill=Reading"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("data-auth-panel", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.Contains("value=\"/Practice?skill=Reading\"", html);
        Assert.DoesNotContain("<html", html);
        Assert.DoesNotContain("site-header", html);

        using var unsafeResponse = await SendModalAsync(client, HttpMethod.Get,
            path + "?returnUrl=" + Uri.EscapeDataString("https://attacker.example/steal"));
        Assert.Equal(HttpStatusCode.OK, unsafeResponse.StatusCode);
        Assert.DoesNotContain("attacker.example", await unsafeResponse.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    public async Task ModalAccountForm_InvalidInput_ReturnsValidationInPanel(string path)
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        var token = await IdentityTestHelpers.GetTokenAsync(client, path);
        using var response = await SendModalAsync(client, HttpMethod.Post, path,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = "invalid-email", ["ReturnUrl"] = "/Practice",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("data-auth-panel", html);
        Assert.Contains("Email không hợp lệ.", html);
        Assert.Contains("value=\"invalid-email\"", html);
        Assert.Contains("value=\"/Practice\"", html);
        Assert.DoesNotContain("<html", html);
        using var profile = await client.GetAsync("/Profile");
        Assert.Equal(HttpStatusCode.Redirect, profile.StatusCode);
    }

    [Fact]
    public async Task ModalLogin_InvalidCredentials_ReturnsValidationWithoutLoggingIn()
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);
        var token = await IdentityTestHelpers.GetTokenAsync(client, "/Account/Login");
        using var response = await SendModalAsync(client, HttpMethod.Post, "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = user.Email!, ["Password"] = "Wrong_123!",
                ["ReturnUrl"] = "https://attacker.example/steal", ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("data-auth-panel", html);
        Assert.Contains("Không thể đăng nhập", html);
        Assert.DoesNotContain("attacker.example", html);
        using var profile = await client.GetAsync("/Profile");
        Assert.Equal(HttpStatusCode.Redirect, profile.StatusCode);
    }

    [Theory]
    [InlineData(AppRoles.Student, null, "/Learning/Dashboard")]
    [InlineData(AppRoles.Teacher, null, "/TeacherCourses")]
    [InlineData(AppRoles.Admin, null, "/Admin/Transactions")]
    [InlineData(AppRoles.Student, "/Practice?skill=Reading", "/Practice?skill=Reading")]
    [InlineData(AppRoles.Student, "https://attacker.example/steal", "/Learning/Dashboard")]
    [InlineData(AppRoles.Student, "//attacker.example/steal", "/Learning/Dashboard")]
    [InlineData(AppRoles.Student, "/", "/Learning/Dashboard")]
    public async Task ModalLogin_Success_ReturnsServerValidatedRedirect_AndAuthenticates(
        string role, string? returnUrl, string expectedRedirect)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        using var client = IdentityTestHelpers.CreateClient(factory);
        var token = await IdentityTestHelpers.GetTokenAsync(client, "/Account/Login");
        using var response = await SendModalAsync(client, HttpMethod.Post, "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = user.Email!, ["Password"] = IdentityTestHelpers.Password,
                ["ReturnUrl"] = returnUrl ?? string.Empty, ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedRedirect, result.RootElement.GetProperty("redirectUrl").GetString());
        using var profile = await client.GetAsync("/Profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }

    [Fact]
    public async Task ModalRegister_Success_OpensLoginWithNotice_AndPreservesReturnUrl()
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        var token = await IdentityTestHelpers.GetTokenAsync(client, "/Account/Register");
        var email = $"modal-register-{Guid.NewGuid():N}@example.test";
        const string returnUrl = "/Practice?skill=Reading";
        using var response = await SendModalAsync(client, HttpMethod.Post, "/Account/Register",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["FullName"] = "Học viên popup", ["Email"] = email,
                ["Password"] = IdentityTestHelpers.Password, ["ConfirmPassword"] = IdentityTestHelpers.Password,
                ["ReturnUrl"] = returnUrl, ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login?returnUrl=", response.Headers.Location!.ToString());
        using var loginPage = await SendModalAsync(client, HttpMethod.Get, response.Headers.Location.ToString());
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        var html = WebUtility.HtmlDecode(await loginPage.Content.ReadAsStringAsync());
        Assert.Contains("data-auth-panel", html);
        Assert.Contains("Đăng ký thành công. Vui lòng đăng nhập.", html);
        Assert.Contains("action=\"/Account/Login\"", html);
        Assert.Contains($"value=\"{returnUrl}\"", html);
        Assert.DoesNotContain("<html", html);

        var loginToken = Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(loginToken));
        using var login = await SendModalAsync(client, HttpMethod.Post, "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = email, ["Password"] = IdentityTestHelpers.Password,
                ["ReturnUrl"] = returnUrl, ["__RequestVerificationToken"] = loginToken
            }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var result = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        Assert.Equal(returnUrl, result.RootElement.GetProperty("redirectUrl").GetString());
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    public async Task ModalAccountMutation_WithoutAntiforgeryToken_IsRejected(string path)
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await SendModalAsync(client, HttpMethod.Post, path,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = "guest@example.test", ["Password"] = IdentityTestHelpers.Password
            }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

        using var root = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, root.StatusCode);
        Assert.Equal("/Learning/Dashboard", root.Headers.Location!.ToString());
        Assert.Contains("Nguyễn Khang", WebUtility.HtmlDecode(await client.GetStringAsync("/Learning/Dashboard")));
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
    [InlineData("https://example.test/steal", "/Learning/Dashboard")]
    [InlineData("/", "/Learning/Dashboard")]
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

    private static async Task<HttpResponseMessage> SendModalAsync(
        HttpClient client, HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("X-Auth-Modal", "true");
        return await client.SendAsync(request);
    }
}
