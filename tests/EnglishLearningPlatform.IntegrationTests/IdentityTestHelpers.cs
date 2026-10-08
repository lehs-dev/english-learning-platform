using System.Net;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

internal static class IdentityTestHelpers
{
    internal const string Password = "TestOnly_123!";

    internal static HttpClient CreateClient(WebApplicationFactory<Program> factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    internal static async Task<ApplicationUser> CreateUserAsync(WebApplicationFactory<Program> factory, string role)
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { FullName = $"{role} Test", Email = email, UserName = email };
        Assert.True((await manager.CreateAsync(user, Password)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    internal static async Task<string> GetTokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Form phải có antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    internal static async Task<HttpResponseMessage> LoginAsync(HttpClient client, ApplicationUser user,
        string? returnUrl = null, string password = Password)
    {
        var token = await GetTokenAsync(client, "/Account/Login");
        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = user.Email!, ["Password"] = password,
            ["RememberMe"] = "false", ["ReturnUrl"] = returnUrl ?? string.Empty,
            ["__RequestVerificationToken"] = token
        }));
    }
}
