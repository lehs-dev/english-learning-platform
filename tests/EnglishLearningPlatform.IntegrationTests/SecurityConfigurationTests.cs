using System.Reflection;
using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Infrastructure;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Web.Controllers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class SecurityConfigurationTests
{
    [Theory]
    [InlineData("Production", CookieSecurePolicy.Always)]
    [InlineData("Staging", CookieSecurePolicy.Always)]
    [InlineData("Development", CookieSecurePolicy.SameAsRequest)]
    public void AuthenticationCookie_HasRequiredFlags(string environment, CookieSecurePolicy secure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment(environment));
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=SecurityConfiguration;Integrated Security=true;TrustServerCertificate=true"
        }).Build());
        using var provider = services.BuildServiceProvider();
        var cookie = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, cookie.Cookie.SameSite);
        Assert.Equal(secure, cookie.Cookie.SecurePolicy);
        Assert.Equal(typeof(AccountStatusCookieEvents), cookie.EventsType);
    }

    [Fact]
    public void AllDeclaredMutationEndpoints_RequireAntiforgery_ExceptBoundedSignedWebhook()
    {
        var exceptions = new List<MethodInfo>();
        foreach (var controller in typeof(AccountController).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t)))
        foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var methods = action.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods);
            if (!methods.Any(m => m is "POST" or "PUT" or "PATCH" or "DELETE")) continue;
            if (action.IsDefined(typeof(IgnoreAntiforgeryTokenAttribute)))
            {
                exceptions.Add(action);
                Assert.Equal(typeof(CheckoutController), controller);
                Assert.Equal("/payments/webhook", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
                Assert.Equal(16384L, action.CustomAttributes.Single(a => a.AttributeType == typeof(RequestSizeLimitAttribute)).ConstructorArguments.Single().Value);
            }
            else Assert.True(action.IsDefined(typeof(ValidateAntiForgeryTokenAttribute)), $"{controller.Name}.{action.Name} thiếu antiforgery.");
        }
        Assert.Single(exceptions);
    }

    [Fact]
    public void AdminSurface_IsReadOnly_AndProjectionExcludesIdentitySecretsAndAnswers()
    {
        foreach (var action in typeof(AdminController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            Assert.Equal(new[] { "GET" }, action.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods));
        var properties = new[] { typeof(AdminUser), typeof(AdminTransaction) }.SelectMany(t => t.GetProperties()).Select(p => p.Name);
        Assert.DoesNotContain(properties, p => p.Contains("Password", StringComparison.OrdinalIgnoreCase) || p.Contains("Secret", StringComparison.OrdinalIgnoreCase) || p is "IsCorrect" or "SecurityStamp" or "ConcurrencyStamp");
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "SecurityTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
