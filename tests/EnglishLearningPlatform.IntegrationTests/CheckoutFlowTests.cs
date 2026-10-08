using System.Net;
using System.Net.Http.Json;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Commerce;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed partial class CheckoutFlowTests(CheckoutTestFactory factory) : IClassFixture<CheckoutTestFactory>
{
    private async Task<(ApplicationUser Student, Course Course)> Setup(bool paid = true, CourseStatus status = CourseStatus.Published)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var course = new Course
        {
            OwnerTeacherUserId = teacher.Id, Title = "Checkout " + Guid.NewGuid().ToString("N"),
            IsPaid = paid, Price = paid ? 199000 : 0, Status = status,
            Modules = [new Module { Title = "Module", Lessons = [new Lesson { Title = "Lesson", Status = LessonStatus.Published,
                Resources = [new LessonResource { ResourceType = LessonResourceType.Text, ContentText = "Lesson content" }] }] }]
        };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Courses.Add(course); await db.SaveChangesAsync();
        return (student, course);
    }

    private async Task<OrderCheckout> Create(ApplicationUser student, Course course)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICheckoutService>().CreateOrderAsync(student.Id, course.Id);
        Assert.Equal(CheckoutCode.Allowed, result.Code);
        return result.Value!;
    }

    private SignedPaymentCallback Event(OrderCheckout order, SandboxScenario scenario = SandboxScenario.Success) =>
        factory.Services.GetRequiredService<IPaymentGateway>().CreateCallback(order, scenario);

    private SignedPaymentCallback Sign(PaymentCallback data) => factory.Services.GetRequiredService<LocalSandboxGateway>().Sign(data);

    private async Task<CheckoutCode> Process(SignedPaymentCallback callback)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICheckoutService>().ProcessCallbackAsync(callback);
    }

    private async Task AssertCounts(Guid courseId, int orders, int payments, int enrollments)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(orders, await db.Orders.CountAsync(o => o.CourseId == courseId));
        Assert.Equal(payments, await db.Payments.CountAsync(p => p.Order.CourseId == courseId));
        Assert.Equal(enrollments, await db.Enrollments.CountAsync(e => e.CourseId == courseId));
    }

    [Fact]
    public async Task HttpCheckout_UsesServerBuyerPriceAndCurrency_GetAndRefreshDoNotWrite()
    {
        var (student, course) = await Setup();
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        var path = $"/Checkout?courseId={course.Id}";
        using var preview = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var html = await preview.Content.ReadAsStringAsync();
        Assert.Contains(course.Title, html); Assert.Contains("199.000", html);
        Assert.Contains("không thu tiền thật", html);
        SaveBrowserArtifact("checkout.html", html);
        await AssertCounts(course.Id, 0, 0, 0);

        var token = await IdentityTestHelpers.GetTokenAsync(client, path);
        var form = new Dictionary<string, string>
        {
            ["CourseId"] = course.Id.ToString(), ["Amount"] = "1", ["Currency"] = "USD",
            ["StudentUserId"] = other.Id.ToString(), ["Status"] = "Succeeded", ["__RequestVerificationToken"] = token
        };
        using var post = await client.PostAsync("/Checkout/CreateOrder", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        using var repeat = await client.PostAsync("/Checkout/CreateOrder", new FormUrlEncodedContent(form));
        Assert.Equal(post.Headers.Location, repeat.Headers.Location);
        for (var i = 0; i < 2; i++)
        {
            using var refresh = await client.GetAsync(post.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        }
        using var scope = factory.Services.CreateScope();
        var order = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.SingleAsync(o => o.CourseId == course.Id);
        Assert.Equal(student.Id, order.StudentUserId); Assert.Equal(199000, order.Amount); Assert.Equal("VND", order.Currency);
        await AssertCounts(course.Id, 1, 0, 0);
    }

    [Fact]
    public async Task PostRechecksPriceAndEligibilityAfterGet()
    {
        var (student, course) = await Setup();
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Checkout?courseId={course.Id}");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var changed = await db.Courses.SingleAsync(c => c.Id == course.Id); changed.Price = 299000; await db.SaveChangesAsync();
        }
        using var post = await client.PostAsync("/Checkout/CreateOrder", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["CourseId"] = course.Id.ToString(), ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(299000, (await db.Orders.SingleAsync(o => o.CourseId == course.Id)).Amount);
            var changed = await db.Courses.SingleAsync(c => c.Id == course.Id); changed.Status = CourseStatus.Unpublished; await db.SaveChangesAsync();
        }
        using var denied = await client.PostAsync("/Checkout/CreateOrder", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["CourseId"] = course.Id.ToString(), ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        using var unavailable = await client.GetAsync(denied.Headers.Location);
        Assert.Contains("không đủ điều kiện mua", await unavailable.Content.ReadAsStringAsync());
        await AssertCounts(course.Id, 1, 0, 0);
    }

    [Fact]
    public async Task ConcurrentPurchaseClicks_ReuseOneUnprocessedOrder()
    {
        var (student, course) = await Setup();
        var orders = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Create(student, course)));
        Assert.Single(orders.Select(o => o.Id).Distinct());
        await AssertCounts(course.Id, 1, 0, 0);
    }

    [Theory]
    [InlineData(false, CourseStatus.Published)]
    [InlineData(true, CourseStatus.Draft)]
    [InlineData(true, CourseStatus.Unpublished)]
    [InlineData(true, CourseStatus.Archived)]
    public async Task IneligibleCourse_CannotCreateOrder(bool paid, CourseStatus status)
    {
        var (student, course) = await Setup(paid, status);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(CheckoutCode.NotPurchasable, (await scope.ServiceProvider.GetRequiredService<ICheckoutService>()
            .CreateOrderAsync(student.Id, course.Id)).Code);
        await AssertCounts(course.Id, 0, 0, 0);
    }

    [Theory]
    [InlineData(AccountStatus.Locked)]
    [InlineData(AccountStatus.Disabled)]
    public async Task InactiveStudent_CannotPurchase(AccountStatus status)
    {
        var (student, course) = await Setup();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.SingleAsync(u => u.Id == student.Id)).AccountStatus = status; await db.SaveChangesAsync();
        Assert.Equal(CheckoutCode.Forbidden, (await scope.ServiceProvider.GetRequiredService<ICheckoutService>()
            .CreateOrderAsync(student.Id, course.Id)).Code);
        await AssertCounts(course.Id, 0, 0, 0);
    }

    [Fact]
    public async Task DisabledOwnerOrInvalidPrice_CannotPurchase()
    {
        var (student, course) = await Setup();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = await db.Courses.SingleAsync(c => c.Id == course.Id); entity.Price = 0; await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
        Assert.Equal(CheckoutCode.NotPurchasable, (await service.CreateOrderAsync(student.Id, course.Id)).Code);
        entity.Price = 199000;
        (await db.Users.SingleAsync(u => u.Id == course.OwnerTeacherUserId)).AccountStatus = AccountStatus.Disabled; await db.SaveChangesAsync();
        Assert.Equal(CheckoutCode.NotPurchasable, (await service.CreateOrderAsync(student.Id, course.Id)).Code);
        await AssertCounts(course.Id, 0, 0, 0);
    }

    [Fact]
    public async Task HttpAuthorizationOwnershipAndCsrf_AreEnforced()
    {
        var (student, course) = await Setup(); var order = await Create(student, course);
        using var guest = IdentityTestHelpers.CreateClient(factory);
        using var anonymous = await guest.GetAsync($"/Checkout?courseId={course.Id}");
        Assert.Contains("/Account/Login", anonymous.Headers.Location!.ToString());
        foreach (var role in new[] { AppRoles.Student, AppRoles.Teacher, AppRoles.Admin })
        {
            var other = await IdentityTestHelpers.CreateUserAsync(factory, role);
            using var client = IdentityTestHelpers.CreateClient(factory); using var login = await IdentityTestHelpers.LoginAsync(client, other);
            using var get = await client.GetAsync($"/Checkout/Order/{order.Id}");
            if (role == AppRoles.Student)
            {
                Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
                var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Checkout?courseId={course.Id}");
                using var simulate = await client.PostAsync($"/Checkout/Simulate/{order.Id}", new FormUrlEncodedContent(new Dictionary<string, string>
                { ["scenario"] = "success", ["__RequestVerificationToken"] = token }));
                Assert.Equal(HttpStatusCode.NotFound, simulate.StatusCode);
            }
            else
            {
                Assert.Contains("/Account/AccessDenied", get.Headers.Location!.ToString());
                using var scope = factory.Services.CreateScope();
                Assert.Equal(CheckoutCode.Forbidden, (await scope.ServiceProvider.GetRequiredService<ICheckoutService>()
                    .CreateOrderAsync(other.Id, course.Id)).Code);
            }
        }
        using var owner = IdentityTestHelpers.CreateClient(factory); using var ownerLogin = await IdentityTestHelpers.LoginAsync(owner, student);
        using var missingToken = await owner.PostAsync("/Checkout/CreateOrder", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["CourseId"] = course.Id.ToString() })); Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        using var missingSimToken = await owner.PostAsync($"/Checkout/Simulate/{order.Id}", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["scenario"] = "success" })); Assert.Equal(HttpStatusCode.BadRequest, missingSimToken.StatusCode);
        await AssertCounts(course.Id, 1, 0, 0);
    }

    [Fact]
    public async Task HttpSandboxSuccess_GrantsExactlyOneEnrollmentAndLearningAccess()
    {
        var (student, course) = await Setup(); var order = await Create(student, course);
        using var client = IdentityTestHelpers.CreateClient(factory); using var login = await IdentityTestHelpers.LoginAsync(client, student);
        var path = $"/Checkout/Order/{order.Id}";
        using var forgedReturn = await client.GetAsync(path + "?status=Succeeded&success=true");
        var pendingHtml = await forgedReturn.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Thanh toán thử nghiệm thành công.", pendingHtml);
        SaveBrowserArtifact("pending.html", pendingHtml);
        await AssertCounts(course.Id, 1, 0, 0);
        var token = await IdentityTestHelpers.GetTokenAsync(client, path);
        using var simulation = await client.PostAsync($"/Checkout/Simulate/{order.Id}", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["scenario"] = "success", ["Status"] = "Failed", ["Amount"] = "1", ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, simulation.StatusCode);
        using var result = await client.GetAsync(simulation.Headers.Location);
        var html = await result.Content.ReadAsStringAsync(); Assert.Contains("Thanh toán thử nghiệm thành công. Bạn đã có thể bắt đầu học.", html); Assert.Contains("Vào học", html);
        SaveBrowserArtifact("success.html", html);
        using var lesson = await client.GetAsync($"/Learning/Lesson/{course.Modules.First().Lessons.First().Id}");
        Assert.Equal(HttpStatusCode.OK, lesson.StatusCode);
        Assert.Equal(CheckoutCode.Allowed, await Process(Event(order)));
        await AssertCounts(course.Id, 1, 1, 1);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var enrollment = await db.Enrollments.Include(e => e.Payment).SingleAsync(e => e.CourseId == course.Id);
        Assert.Equal(student.Id, enrollment.StudentUserId); Assert.Equal(PaymentStatus.Succeeded, enrollment.Payment!.Status);
        Assert.NotNull(enrollment.Payment.PaidAtUtc);
        using var redirect = await client.GetAsync($"/Checkout?courseId={course.Id}");
        Assert.Contains($"/Learning/Course/{course.Id}", redirect.Headers.Location!.ToString());
        Assert.Equal(CheckoutCode.AlreadyEnrolled, (await scope.ServiceProvider.GetRequiredService<ICheckoutService>().CreateOrderAsync(student.Id, course.Id)).Code);
    }

    [Theory]
    [InlineData(SandboxScenario.Failed, PaymentStatus.Failed)]
    [InlineData(SandboxScenario.Cancelled, PaymentStatus.Cancelled)]
    public async Task FailureAndCancellation_DoNotGrantAccess_AndRetryCreatesNewOrder(SandboxScenario scenario, PaymentStatus expected)
    {
        var (student, course) = await Setup(); var order = await Create(student, course);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Checkout/Order/{order.Id}");
        using var simulated = await client.PostAsync($"/Checkout/Simulate/{order.Id}", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["scenario"] = scenario == SandboxScenario.Failed ? "failed" : "cancelled", ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, simulated.StatusCode);
        using var resultPage = await client.GetAsync($"/Checkout/Order/{order.Id}");
        var resultHtml = await resultPage.Content.ReadAsStringAsync();
        Assert.Contains(expected == PaymentStatus.Failed ? "role=\"alert\"" : "role=\"status\"", resultHtml);
        SaveBrowserArtifact(expected == PaymentStatus.Failed ? "failed.html" : "cancelled.html", resultHtml);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(expected, (await scope.ServiceProvider.GetRequiredService<ICheckoutService>().GetOrderAsync(student.Id, order.Id)).Value!.Status);
        Assert.Equal(LearningAccessResult.Forbidden, (await scope.ServiceProvider.GetRequiredService<ICourseService>()
            .OpenLessonAsync(student.Id, course.Modules.First().Lessons.First().Id)).Access);
        var next = await Create(student, course); Assert.NotEqual(order.Id, next.Id);
        await AssertCounts(course.Id, 2, 1, 0);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("order")]
    [InlineData("provider")]
    [InlineData("transaction")]
    [InlineData("pending")]
    [InlineData("tampered")]
    public async Task InvalidAuthenticatedOrTamperedCallback_CannotGrantAccess(string invalid)
    {
        var (student, course) = await Setup(); var order = await Create(student, course); var callback = Event(order);
        callback = invalid switch
        {
            "signature" => callback with { Signature = "invalid" },
            "amount" => Sign(callback.Data with { Amount = 1 }),
            "currency" => Sign(callback.Data with { Currency = "USD" }),
            "order" => Sign(callback.Data with { OrderId = Guid.NewGuid() }),
            "provider" => Sign(callback.Data with { Provider = "other-provider" }),
            "transaction" => Sign(callback.Data with { ProviderTransactionId = "" }),
            "pending" => Sign(callback.Data with { Status = PaymentStatus.Pending }),
            _ => callback with { Data = callback.Data with { Amount = 1 } }
        };
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.PostAsJsonAsync("/payments/webhook", callback);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertCounts(course.Id, 1, 0, 0);
    }

    [Fact]
    public async Task CallbackWithAnotherOrdersIdAndOriginalAmount_IsRejected()
    {
        var (student, course) = await Setup(); var original = await Create(student, course);
        var (otherStudent, otherCourse) = await Setup();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Courses.SingleAsync(c => c.Id == otherCourse.Id)).Price = 299000; await db.SaveChangesAsync();
        }
        var other = await Create(otherStudent, otherCourse);
        Assert.Equal(CheckoutCode.InvalidCallback, await Process(Sign(Event(original).Data with { OrderId = other.Id })));
        await AssertCounts(course.Id, 1, 0, 0); await AssertCounts(otherCourse.Id, 1, 0, 0);
    }

    [Fact]
    public async Task SequentialAndConcurrentDuplicateCallbacks_GrantOnce()
    {
        var (student, course) = await Setup(); var order = await Create(student, course); var callback = Event(order);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Process(callback)));
        Assert.All(results, code => Assert.Equal(CheckoutCode.Allowed, code));
        Assert.Equal(CheckoutCode.Allowed, await Process(callback));
        await AssertCounts(course.Id, 1, 1, 1);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(LearningAccessResult.Allowed, (await scope.ServiceProvider.GetRequiredService<ICourseService>()
            .OpenLessonAsync(student.Id, course.Modules.First().Lessons.First().Id)).Access);
    }

    [Fact]
    public async Task DuplicateTransactionWithDifferentData_IsRejectedAcrossOrders()
    {
        var (student, course) = await Setup(); var order = await Create(student, course); var callback = Event(order);
        Assert.Equal(CheckoutCode.Allowed, await Process(callback));
        Assert.Equal(CheckoutCode.Conflict, await Process(Sign(callback.Data with { Status = PaymentStatus.Failed })));
        var (otherStudent, otherCourse) = await Setup(); var other = await Create(otherStudent, otherCourse);
        Assert.Equal(CheckoutCode.Conflict, await Process(Sign(callback.Data with { OrderId = other.Id })));
        await AssertCounts(course.Id, 1, 1, 1); await AssertCounts(otherCourse.Id, 1, 0, 0);
    }

    [Theory]
    [InlineData(SandboxScenario.Failed)]
    [InlineData(SandboxScenario.Cancelled)]
    [InlineData(SandboxScenario.Success)]
    public async Task LateCallbackCannotOverwriteTerminalStatus(SandboxScenario first)
    {
        var (student, course) = await Setup(); var order = await Create(student, course); var callback = Event(order, first);
        Assert.Equal(CheckoutCode.Allowed, await Process(callback));
        var changedStatus = first == SandboxScenario.Success ? PaymentStatus.Failed : PaymentStatus.Succeeded;
        Assert.Equal(CheckoutCode.Conflict, await Process(Sign(callback.Data with { Status = changedStatus, ProviderTransactionId = "late-" + Guid.NewGuid().ToString("N") })));
        using var scope = factory.Services.CreateScope();
        Assert.Equal(callback.Data.Status, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Payments.SingleAsync(p => p.OrderId == order.Id)).Status);
        await AssertCounts(course.Id, 1, 1, first == SandboxScenario.Success ? 1 : 0);
    }

    [Fact]
    public async Task ConcurrentDifferentEvents_FirstTerminalWins()
    {
        var (student, course) = await Setup(); var order = await Create(student, course); var success = Event(order);
        var failure = Sign(success.Data with { Status = PaymentStatus.Failed, ProviderTransactionId = "different-" + Guid.NewGuid().ToString("N") });
        var codes = await Task.WhenAll(Process(success), Process(failure));
        Assert.Contains(CheckoutCode.Allowed, codes); Assert.Contains(CheckoutCode.Conflict, codes);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payment = await db.Payments.SingleAsync(p => p.OrderId == order.Id);
        await AssertCounts(course.Id, 1, 1, payment.Status == PaymentStatus.Succeeded ? 1 : 0);
    }

    [Fact]
    public async Task EnrollmentWriteFailure_RollsBackPreviouslySavedPayment_AndRetryWorks()
    {
        var (student, course) = await Setup(); var order = await Create(student, course); var callback = Event(order);
        factory.Faults.FailEnrollment = true;
        try { Assert.Equal(CheckoutCode.IntegrationError, await Process(callback)); }
        finally { factory.Faults.FailEnrollment = false; }
        Assert.True(factory.Faults.SawSavedPayment);
        await AssertCounts(course.Id, 1, 0, 0);
        using (var scope = factory.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs.AnyAsync(a => a.AfterData!.Contains(order.Id.ToString("N"))));
        Assert.Equal(CheckoutCode.Allowed, await Process(callback));
        await AssertCounts(course.Id, 1, 1, 1);
    }

    [Fact]
    public async Task FreeEnrollmentAndExistingPaidAccess_ArePreserved()
    {
        var (student, free) = await Setup(false);
        using (var scope = factory.Services.CreateScope())
        {
            var learning = scope.ServiceProvider.GetRequiredService<ICourseService>();
            Assert.True((await learning.EnrollFreeAsync(student.Id, free.Id)).Success);
            Assert.Equal(LearningAccessResult.Allowed, (await learning.OpenLessonAsync(student.Id, free.Modules.First().Lessons.First().Id)).Access);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await db.Courses.SingleAsync(c => c.Id == free.Id); course.IsPaid = true; course.Price = 299000; await db.SaveChangesAsync();
            Assert.Equal(CheckoutCode.AlreadyEnrolled, (await scope.ServiceProvider.GetRequiredService<ICheckoutService>().CreateOrderAsync(student.Id, free.Id)).Code);
        }
        await AssertCounts(free.Id, 0, 0, 1);
        var (buyer, paid) = await Setup(); var order = await Create(buyer, paid);
        Assert.Equal(CheckoutCode.Allowed, await Process(Event(order)));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await db.Courses.SingleAsync(c => c.Id == paid.Id); course.Price = 399000; course.Status = CourseStatus.Archived; await db.SaveChangesAsync();
            Assert.Equal(LearningAccessResult.Allowed, (await scope.ServiceProvider.GetRequiredService<ICourseService>()
                .OpenLessonAsync(buyer.Id, paid.Modules.First().Lessons.First().Id)).Access);
            Assert.Equal(CheckoutCode.AlreadyEnrolled, (await scope.ServiceProvider.GetRequiredService<ICheckoutService>().PreviewAsync(buyer.Id, paid.Id)).Code);
        }
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    public async Task SandboxIsBlockedWhenDisabledOrOutsideDevelopment(string environment, bool enabled)
    {
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["CheckoutSandbox:Enabled"] = enabled.ToString(), ["LearningDemo:Enabled"] = "false" }));
        });
        var (student, course) = await Setup();
        using var scope = host.Services.CreateScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<IPaymentGateway>().IsEnabled);
        Assert.Equal(CheckoutCode.Disabled, (await scope.ServiceProvider.GetRequiredService<ICheckoutService>().CreateOrderAsync(student.Id, course.Id)).Code);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var callback = await client.PostAsJsonAsync("/payments/webhook", new SignedPaymentCallback(
            new(Guid.NewGuid(), 199000, "VND", LocalSandboxGateway.ProviderName, "test", PaymentStatus.Succeeded), "invalid"));
        Assert.Equal(HttpStatusCode.NotFound, callback.StatusCode);
        await AssertCounts(course.Id, 0, 0, 0);
    }

    [Fact]
    public async Task HttpSignedCallbackAcceptedAndConflictingReplayRejected()
    {
        var (student, course) = await Setup(); var order = await Create(student, course); var callback = Event(order);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var success = await client.PostAsJsonAsync("/payments/webhook", callback);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        using var replay = await client.PostAsJsonAsync("/payments/webhook", callback);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var conflict = await client.PostAsJsonAsync("/payments/webhook", Sign(callback.Data with { Status = PaymentStatus.Cancelled }));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await AssertCounts(course.Id, 1, 1, 1);
    }

    // Optional artifacts from real HTTP responses for a separate browser probe; never used as snapshots/assertions.
    private static void SaveBrowserArtifact(string name, string html)
    {
        var directory = Environment.GetEnvironmentVariable("CHECKOUT_BROWSER_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), html);
    }

    [Fact]
    public async Task HttpSimulationFailure_ShowsErrorPreservesOrderAndCanRetry()
    {
        var (student, course) = await Setup(); var order = await Create(student, course);
        using var client = IdentityTestHelpers.CreateClient(factory); using var login = await IdentityTestHelpers.LoginAsync(client, student);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Checkout/Order/{order.Id}");
        var form = new Dictionary<string, string> { ["scenario"] = "success", ["__RequestVerificationToken"] = token };
        factory.Faults.FailEnrollment = true;
        try
        {
            using var failed = await client.PostAsync($"/Checkout/Simulate/{order.Id}", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.Redirect, failed.StatusCode);
            using var page = await client.GetAsync(failed.Headers.Location);
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains("role=\"alert\"", html); Assert.Contains("Chưa thể xử lý thanh toán thử nghiệm", WebUtility.HtmlDecode(html));
            Assert.Contains(order.Id.ToString(), html); Assert.Contains("Mô phỏng thành công", html);
            SaveBrowserArtifact("error.html", html);
        }
        finally { factory.Faults.FailEnrollment = false; }
        await AssertCounts(course.Id, 1, 0, 0);
        using var retry = await client.PostAsync($"/Checkout/Simulate/{order.Id}", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, retry.StatusCode);
        await AssertCounts(course.Id, 1, 1, 1);
    }

    [Fact]
    public async Task CreatedOrderKeepsOriginalQuoteAfterPriceChanges()
    {
        var (student, course) = await Setup(); var order = await Create(student, course);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Courses.SingleAsync(c => c.Id == course.Id)).Price = 399000; await db.SaveChangesAsync();
        }
        Assert.Equal(CheckoutCode.Allowed, await Process(Event(order)));
        await AssertCounts(course.Id, 1, 1, 1);
    }

    [Fact]
    public async Task TwoQuotedOrdersPaidConcurrently_DoNotDuplicateEnrollment()
    {
        var (student, course) = await Setup(); var first = await Create(student, course);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Courses.SingleAsync(c => c.Id == course.Id)).Price = 299000; await db.SaveChangesAsync();
        }
        var second = await Create(student, course); Assert.NotEqual(first.Id, second.Id);
        var result = await Task.WhenAll(Process(Event(first)), Process(Event(second)));
        Assert.All(result, code => Assert.Equal(CheckoutCode.Allowed, code));
        await AssertCounts(course.Id, 2, 2, 1);
    }

    [Fact]
    public async Task MatchingReplayRepairsLegacyPaymentWithoutEnrollment()
    {
        var (student, course) = await Setup(); var order = await Create(student, course); var callback = Event(order);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Payments.Add(new Payment { OrderId = order.Id, Amount = order.Amount, Provider = callback.Data.Provider,
                ProviderTransactionId = callback.Data.ProviderTransactionId, Status = PaymentStatus.Succeeded });
            await db.SaveChangesAsync();
        }
        Assert.Equal(CheckoutCode.Allowed, await Process(callback));
        Assert.Equal(CheckoutCode.Allowed, await Process(callback));
        await AssertCounts(course.Id, 1, 1, 1);
    }
}
