using System.Reflection;
using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Application.Authorization;
using EnglishLearningPlatform.Infrastructure;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Web.Controllers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
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
    [Fact]
    public void VnpayCallbacks_AreExplicitAnonymousGets_AndOrderResultRemainsProtected()
    {
        var controller = typeof(VnpayController);
        Assert.True(controller.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        var ipn = controller.GetMethod(nameof(VnpayController.Ipn))!;
        Assert.True(ipn.IsDefined(typeof(AllowAnonymousAttribute)));
        Assert.Equal("/payments/vnpay/ipn", ipn.GetCustomAttribute<HttpGetAttribute>()!.Template);
        var browserReturn = controller.GetMethod(nameof(VnpayController.Return))!;
        Assert.Equal("/payments/vnpay/return", browserReturn.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.True(browserReturn.IsDefined(typeof(AllowAnonymousAttribute)));
        Assert.Equal(AuthorizationPolicies.Student, typeof(CheckoutController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }

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
    public void AdminSurface_OnlyAllowsReviewedAccountMutations_WithAdminPolicyAndAntiforgery()
    {
        var controller = typeof(AdminController);
        Assert.Equal(AuthorizationPolicies.Admin, controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.True(controller.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var endpoints = actions.SelectMany(action => action.GetCustomAttributes<HttpMethodAttribute>()
            .SelectMany(attribute => attribute.HttpMethods.Select(verb => $"{action.Name}:{verb}"))).Order().ToArray();
        Assert.Equal(new[] { "Audit:GET", "Courses:GET", "CreateTeacher:GET", "CreateTeacher:POST", "CreateUser:GET", "CreateUser:POST", "Enrollments:GET", "Index:GET",
            "Revenue:GET", "Transactions:GET", "UpdateUser:POST", "Users:GET" }, endpoints);
        foreach (var action in actions)
        {
            Assert.False(action.IsDefined(typeof(AllowAnonymousAttribute)));
            Assert.False(action.IsDefined(typeof(IgnoreAntiforgeryTokenAttribute)));
            if (action.IsDefined(typeof(HttpPostAttribute)))
                Assert.True(action.IsDefined(typeof(ValidateAntiForgeryTokenAttribute)), $"{action.Name} requires antiforgery.");
        }
    }

    [Fact]
    public void AdminProjections_ExcludeIdentitySecretsAndAssessmentAnswers()
    {
        var properties = new[] { typeof(AdminUser), typeof(AdminTransaction), typeof(AdminAudit), typeof(AdminChangeResult),
            typeof(AdminDashboardSummary), typeof(AdminCourseOverview), typeof(AdminEnrollmentOverview),
            typeof(AdminRevenueReport), typeof(AdminRevenueSummary), typeof(AdminRevenuePeriod) }
            .SelectMany(t => t.GetProperties()).Select(p => p.Name);
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
