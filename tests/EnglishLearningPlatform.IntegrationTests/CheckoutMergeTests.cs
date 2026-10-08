using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Commerce;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed partial class CheckoutFlowTests
{
    [Theory]
    [InlineData("local-sandbox")]
    [InlineData("LOCAL-SANDBOX")]
    public async Task ReservedLocalProviderCannotConfigureHostedCheckout(string provider)
    {
        var (student, course) = await Setup();
        var options = factory.Services.GetRequiredService<IOptions<HostedPaymentOptions>>().Value;
        options.Enabled = true; options.Provider = provider;
        options.SigningSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        options.CheckoutUrl = "https://gateway.example.test/checkout";
        options.PublicBaseUrl = "https://learning.example.test";
        try
        {
            using var scope = factory.Services.CreateScope();
            var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>();
            Assert.False(gateway.IsConfigured);
            var result = await scope.ServiceProvider.GetRequiredService<ICheckoutService>()
                .StartAsync(student.Id, course.Id);
            Assert.NotNull(result.Error); Assert.Null(result.OrderId);
            await AssertCounts(course.Id, 0, 0, 0);
        }
        finally { options.Enabled = false; }
    }

    [Fact]
    public async Task HostedOrderCannotBeSettledByLocalSimulatorOrLocalSignature()
    {
        var (student, course) = await Setup();
        var options = factory.Services.GetRequiredService<IOptions<HostedPaymentOptions>>().Value;
        options.Enabled = true; options.Provider = "MergeHosted";
        options.SigningSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        options.CheckoutUrl = "https://gateway.example.test/checkout";
        options.PublicBaseUrl = "https://learning.example.test";
        try
        {
            var order = await Create(student, course); Assert.False(order.IsSandbox);
            using var scope = factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
            Assert.Equal(CheckoutCode.Forbidden, await service.SimulateAsync(student.Id, order.Id, SandboxScenario.Success));
            Assert.Equal(CheckoutCode.InvalidCallback, await service.ProcessCallbackAsync(Event(order)));
            var body = JsonSerializer.SerializeToUtf8Bytes(new PaymentNotification(order.Id, "hosted-" + Guid.NewGuid().ToString("N"),
                order.Amount, "VND", PaymentStatus.Succeeded, DateTimeOffset.UtcNow), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(PaymentConfirmation.Invalid, await service.ConfirmAsync(body, Event(order).Signature));
            await AssertCounts(course.Id, 1, 0, 0);
            using var client = IdentityTestHelpers.CreateClient(factory); using var login = await IdentityTestHelpers.LoginAsync(client, student);
            using var page = await client.GetAsync($"/Checkout/Result/{order.Id}");
            var html = await page.Content.ReadAsStringAsync();
            Assert.DoesNotContain("checkout-test-label", html); Assert.DoesNotContain("checkout-tools", html);
            Assert.Contains("https://gateway.example.test/checkout", html);
            using var localResponse = await client.PostAsync("/payments/webhook", new StringContent(
                JsonSerializer.Serialize(Event(order), new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, localResponse.StatusCode);
            var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.SigningSecret), body));
            using var request = new HttpRequestMessage(HttpMethod.Post, "/payments/webhook") { Content = new ByteArrayContent(body) };
            request.Headers.Add("X-Payment-Signature", signature);
            using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await AssertCounts(course.Id, 1, 1, 1);
        }
        finally { options.Enabled = false; }
    }

    [Fact]
    public async Task HostedStartDoesNotFallBackToLocalWhenHostedIsDisabled()
    {
        var (student, course) = await Setup();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
        var result = await service.StartAsync(student.Id, course.Id);
        Assert.NotNull(result.Error); Assert.Null(result.OrderId);
        await AssertCounts(course.Id, 0, 0, 0);
        Assert.Equal(CheckoutCode.Allowed, (await service.CreateOrderAsync(student.Id, course.Id)).Code);
        await AssertCounts(course.Id, 1, 0, 0);
    }

    [Fact]
    public async Task WithdrawnCourseRecordsLocalReceiptWithoutIssuingEnrollment()
    {
        var (student, course) = await Setup(); var order = await Create(student, course); var receipt = Event(order);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Courses.Where(c => c.Id == course.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CourseStatus.Archived));
        Assert.Equal(CheckoutCode.Allowed, await Process(receipt));
        await AssertCounts(course.Id, 1, 1, 0);
    }

    [Fact]
    public async Task LocalQuoteExpiryStopsSimulationButAllowsReceiptOccurredWithinDeadline()
    {
        var (student, course) = await Setup(); var order = await Create(student, course); var receipt = Event(order);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAtUtc, order.CreatedAtUtc.AddTicks(1)));
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
            Assert.False((await service.GetOrderAsync(student.Id, order.Id)).Value!.CanCheckout);
            Assert.Equal(CheckoutCode.NotPurchasable, await service.SimulateAsync(student.Id, order.Id, SandboxScenario.Success));
        }
        Assert.Equal(CheckoutCode.Allowed, await Process(receipt));
        await AssertCounts(course.Id, 1, 1, 1);
    }

    [Fact]
    public async Task ProductionAllowsConfiguredHostedButNeverLocalSimulation()
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CheckoutSandbox:Enabled"] = "true", ["Payments:Enabled"] = "true", ["Payments:Provider"] = "ProductionFixture",
                ["Payments:SigningSecret"] = key, ["Payments:CheckoutUrl"] = "https://gateway.example.test/checkout",
                ["Payments:PublicBaseUrl"] = "https://learning.example.test", ["LearningDemo:Enabled"] = "false"
            }));
        });
        var (student, course) = await Setup(); using var scope = host.Services.CreateScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<LocalSandboxGateway>().IsEnabled);
        var service = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
        var result = await service.CreateOrderAsync(student.Id, course.Id);
        Assert.Equal(CheckoutCode.Allowed, result.Code); Assert.False(result.Value!.IsSandbox);
        Assert.Equal(CheckoutCode.Disabled, await service.SimulateAsync(student.Id, result.Value.Id, SandboxScenario.Success));
        await AssertCounts(course.Id, 1, 0, 0);
    }
}
