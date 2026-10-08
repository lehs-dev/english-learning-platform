using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Commerce;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class CheckoutFeatureTests : IClassFixture<IntegrationTestFactory>
{
    private const string Secret = "Test_only_external_gateway_secret_12345";
    private readonly IntegrationTestFactory factory;
    public CheckoutFeatureTests(IntegrationTestFactory factory)
    {
        this.factory = factory;
        var options = factory.Services.GetRequiredService<IOptions<HostedPaymentOptions>>().Value;
        options.Enabled = true; options.Provider = "TestHosted"; options.SigningSecret = Secret;
        options.CheckoutUrl = "https://gateway.example.test/checkout"; options.PublicBaseUrl = "https://learning.example.test";
    }

    private async Task<(ApplicationUser Student, Course Course)> Seed()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Paid", Status = CourseStatus.Published, IsPaid = true, Price = 199000 };
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Courses.Add(course); await db.SaveChangesAsync(); return (student, course);
    }

    private static byte[] Body(Guid orderId, string transaction, decimal amount = 199000, PaymentStatus status = PaymentStatus.Succeeded, string currency = "VND") =>
        JsonSerializer.SerializeToUtf8Bytes(new PaymentNotification(orderId, transaction, amount, currency, status, DateTimeOffset.UtcNow), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static string Sign(byte[] body) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), body));
    private async Task<CheckoutStart> Start(Guid student, Guid course)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICheckoutService>().StartAsync(student, course);
    }
    private async Task<PaymentConfirmation> Confirm(byte[] body, string? signature = null)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICheckoutService>().ConfirmAsync(body, signature ?? Sign(body));
    }

    [Fact]
    public async Task PaidCheckout_RequiresVerifiedPayment_ReusesPendingOrder_AndConfirmsOnceUnderConcurrency()
    {
        var data = await Seed();
        var starts = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Start(data.Student.Id, data.Course.Id)));
        var start = starts[0]; Assert.NotNull(start.OrderId); Assert.All(starts, s => Assert.Equal(start.OrderId, s.OrderId));
        Assert.Contains("https://gateway.example.test/checkout", start.CheckoutUrl);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Enrollments.AnyAsync(e => e.CourseId == data.Course.Id));
            Assert.False(await db.Payments.AnyAsync(p => p.OrderId == start.OrderId));
            Assert.Empty((await scope.ServiceProvider.GetRequiredService<ICourseService>().MyCoursesAsync(data.Student.Id, false)).Value!);
        }
        var body = Body(start.OrderId!.Value, Guid.NewGuid().ToString());
        Assert.Equal(PaymentConfirmation.Invalid, await Confirm(body, new string('0', 64)));
        Assert.Equal(PaymentConfirmation.Invalid, await Confirm(Body(start.OrderId.Value, "wrong-amount", 1)));
        Assert.Equal(PaymentConfirmation.Invalid, await Confirm(Body(start.OrderId.Value, "wrong-currency", currency: "USD")));
        var confirmations = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Confirm(body)));
        Assert.All(confirmations, result => Assert.Equal(PaymentConfirmation.Accepted, result));
        using var finalScope = factory.Services.CreateScope(); var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await finalDb.Payments.CountAsync(p => p.OrderId == start.OrderId));
        var enrollment = await finalDb.Enrollments.Include(e => e.Payment).SingleAsync(e => e.CourseId == data.Course.Id);
        Assert.Equal(data.Student.Id, enrollment.StudentUserId); Assert.Equal(PaymentStatus.Succeeded, enrollment.Payment!.Status);
        Assert.True((await Start(data.Student.Id, data.Course.Id)).AlreadyEnrolled);
        Assert.Equal(PaymentConfirmation.Conflict, await Confirm(Body(start.OrderId.Value, Guid.NewGuid().ToString())));
    }

    [Fact]
    public async Task FailedPaymentAndWithdrawnCourse_DoNotGrantLearningRights()
    {
        var data = await Seed(); var order = await Start(data.Student.Id, data.Course.Id);
        Assert.Equal(PaymentConfirmation.Accepted, await Confirm(Body(order.OrderId!.Value, Guid.NewGuid().ToString(), status: PaymentStatus.Failed)));
        var retry = await Start(data.Student.Id, data.Course.Id); Assert.NotEqual(order.OrderId, retry.OrderId);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Courses.Where(c => c.Id == data.Course.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CourseStatus.Unpublished));
        Assert.Equal(PaymentConfirmation.Accepted, await Confirm(Body(retry.OrderId!.Value, Guid.NewGuid().ToString())));
        Assert.False(await db.Enrollments.AnyAsync(e => e.CourseId == data.Course.Id));
        Assert.Equal(PaymentStatus.Succeeded, (await scope.ServiceProvider.GetRequiredService<ICheckoutService>().GetAsync(data.Student.Id, retry.OrderId.Value)).Value!.Status);
    }

    [Fact]
    public async Task BrowserReturnCannotConfirmPayment_OrdersArePrivate_PostsRequireCsrf_WebhookIsSigned()
    {
        var data = await Seed(); var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory); using var login = await IdentityTestHelpers.LoginAsync(client, data.Student);
        using var missing = await client.PostAsync($"/Checkout/Start/{data.Course.Id}", new FormUrlEncodedContent([])); Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Courses/Details/{data.Course.Id}");
        using var post = await client.PostAsync($"/Checkout/Start/{data.Course.Id}", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["__RequestVerificationToken"] = token, ["Amount"] = "1", ["StudentUserId"] = other.Id.ToString() }));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.Orders.SingleAsync(o => o.CourseId == data.Course.Id);
        Assert.Equal(199000, order.Amount); Assert.Equal(data.Student.Id, order.StudentUserId);
        using var returned = await client.GetAsync($"/Checkout/Result/{order.Id}?status=Succeeded&paid=true"); Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        Assert.False(await db.Enrollments.AnyAsync(e => e.CourseId == data.Course.Id));
        using var otherClient = IdentityTestHelpers.CreateClient(factory); using var otherLogin = await IdentityTestHelpers.LoginAsync(otherClient, other);
        using var privateOrder = await otherClient.GetAsync($"/Checkout/Result/{order.Id}"); Assert.Equal(HttpStatusCode.NotFound, privateOrder.StatusCode);
        using var webhookClient = IdentityTestHelpers.CreateClient(factory);
        var body = Body(order.Id, Guid.NewGuid().ToString());
        using var invalid = await webhookClient.PostAsync("/payments/webhook", new ByteArrayContent(body)); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/payments/webhook") { Content = new ByteArrayContent(body) };
        request.Headers.Add("X-Payment-Signature", Sign(body));
        using var response = await webhookClient.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await db.Enrollments.AnyAsync(e => e.CourseId == data.Course.Id));
    }

    [Fact]
    public async Task GatewayDisabled_InvalidRoleAndDisabledOwnerCannotCreateOrder()
    {
        var data = await Seed();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(LearningAccessResult.Forbidden, (await Start(data.Course.OwnerTeacherUserId, data.Course.Id)).Access);
        var options = factory.Services.GetRequiredService<IOptions<HostedPaymentOptions>>().Value;
        options.Enabled = false;
        try { Assert.NotNull((await Start(data.Student.Id, data.Course.Id)).Error); }
        finally { options.Enabled = true; }
        Assert.False(await db.Orders.AnyAsync(o => o.CourseId == data.Course.Id));
        await db.Users.Where(u => u.Id == data.Course.OwnerTeacherUserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.AccountStatus, AccountStatus.Disabled));
        Assert.Equal(LearningAccessResult.Forbidden, (await Start(data.Student.Id, data.Course.Id)).Access);
    }

    [Fact]
    public async Task Receipt_IsBoundToOriginalOrderQuote_TransactionCannotBeReused_ExpiredEventIsRejected()
    {
        var data = await Seed(); var start = await Start(data.Student.Id, data.Course.Id);
        var transaction = Guid.NewGuid().ToString();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // A price update must not rewrite the quote already sent to the gateway.
        await db.Courses.Where(c => c.Id == data.Course.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Price, 299000));
        Assert.Equal(PaymentConfirmation.Accepted, await Confirm(Body(start.OrderId!.Value, transaction)));
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var next = await Start(other.Id, data.Course.Id);
        Assert.Equal(PaymentConfirmation.Conflict, await Confirm(Body(next.OrderId!.Value, transaction, 299000)));
        await db.Orders.Where(o => o.Id == next.OrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.Equal(PaymentConfirmation.Invalid, await Confirm(Body(next.OrderId.Value, Guid.NewGuid().ToString(), 299000)));
        Assert.False(await db.Enrollments.AnyAsync(e => e.StudentUserId == other.Id && e.CourseId == data.Course.Id));
    }
}
