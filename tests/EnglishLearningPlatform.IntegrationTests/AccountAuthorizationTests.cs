using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class AccountAuthorizationTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public AccountAuthorizationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Logout_WithoutLogin_RedirectsToLogin()
    {
        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

        using var response = await client.PostAsync(
            "/Account/Logout",
            null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        Assert.NotNull(response.Headers.Location);

        Assert.Contains(
            "/Account/Login",
            response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("/Account/Register")]
    [InlineData("/Account/Login")]
    public async Task Guest_AccountForm_HasAntiforgeryToken(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(
            "name=\"__RequestVerificationToken\"",
            html);
    }
}