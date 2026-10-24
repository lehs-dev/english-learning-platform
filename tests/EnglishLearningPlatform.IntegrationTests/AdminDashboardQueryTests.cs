using System.Net;
using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class AdminDashboardQueryTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task Dashboard_CountsEmptyThenMixedData_InSql_WithoutCountingPaymentRetriesTwice()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IAdminDashboardQueryService>();
        // A permitted caller must exist; all business tables and Student/Teacher roles are empty.
        var empty = await service.GetAsync(admin.Id);
        Assert.Equal(new AdminDashboardSummary(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), empty);
        Assert.Null(empty!.CompletionRate);
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var locked = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var disabled = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        (await db.Users.SingleAsync(u => u.Id == locked.Id)).AccountStatus = AccountStatus.Locked;
        (await db.Users.SingleAsync(u => u.Id == disabled.Id)).AccountStatus = AccountStatus.Disabled;
        var courses = Enum.GetValues<CourseStatus>().Select(status => new Course {
            OwnerTeacherUserId = teacher.Id, Title = $"dashboard-{status}", Status = status }).ToArray();
        db.Courses.AddRange(courses);
        var published = courses.Single(c => c.Status == CourseStatus.Published);
        db.Enrollments.AddRange(
            new Enrollment { Course = published, StudentUserId = student.Id, CompletedAtUtc = DateTimeOffset.UtcNow },
            new Enrollment { Course = published, StudentUserId = locked.Id },
            new Enrollment { Course = courses.Single(c => c.Status == CourseStatus.Archived), StudentUserId = student.Id,
                CompletedAtUtc = DateTimeOffset.UtcNow });
        var paid = new Order { Course = published, StudentUserId = student.Id, Amount = 100000 };
        var failed = new Order { Course = published, StudentUserId = student.Id, Amount = 100000 };
        var cancelled = new Order { Course = published, StudentUserId = student.Id, Amount = 100000 };
        var pending = new Order { Course = published, StudentUserId = student.Id, Amount = 100000 };
        db.Orders.AddRange(paid, failed, cancelled, pending);
        void PaymentFor(Order order, PaymentStatus status, int minute) => db.Payments.Add(new Payment {
            Order = order, Status = status, Amount = order.Amount,
            ProviderTransactionId = $"dashboard-{Guid.NewGuid():N}", CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(minute) });
        PaymentFor(paid, PaymentStatus.Failed, -3);
        PaymentFor(paid, PaymentStatus.Succeeded, -2);
        PaymentFor(paid, PaymentStatus.Succeeded, -1);
        // A later unsuccessful record must not undo an already successful order.
        PaymentFor(paid, PaymentStatus.Cancelled, 0);
        PaymentFor(failed, PaymentStatus.Failed, 0);
        PaymentFor(cancelled, PaymentStatus.Cancelled, 0);
        await db.SaveChangesAsync();
        var trackedBefore = db.ChangeTracker.Entries().Count();
        var result = await service.GetAsync(admin.Id);
        Assert.Equal(new AdminDashboardSummary(5, 1, 1, 2, 2, 4, 1, 1, 3, 2, 4, 1, 1, 1), result);
        Assert.Equal(66.7m, result!.CompletionRate);
        Assert.Equal(1, result.IncompleteEnrollments);
        Assert.Equal(trackedBefore, db.ChangeTracker.Entries().Count());
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        using var response = await client.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var html = await response.Content.ReadAsStringAsync();
        foreach (var (metric, count) in new[] { ("students", 2), ("teachers", 2), ("courses", 4),
            ("draft-courses", 1), ("enrollments", 3), ("completed-enrollments", 2), ("succeeded-orders", 1) })
            Assert.Contains($"data-metric=\"{metric}\">{count}</dd>", html);
        Assert.Contains("data-metric=\"published-courses\">1</span>", html);
        Assert.Contains("href=\"/Admin/Courses?status=Draft\"", html);
        Assert.Contains("href=\"/Admin/Enrollments?completed=true\"", html);
        foreach (var role in new[] { AppRoles.Student, AppRoles.Teacher })
        {
            var destination = $"/Admin/Users?role={role}";
            Assert.Contains($"href=\"{destination}\"", html);
            var list = await client.GetStringAsync(destination);
            Assert.Contains($"value=\"{role}\" selected=\"selected\"", list);
            Assert.Contains("class=\"admin-count\">2</span>", list);
        }
    }

}

// Separate fixture so the empty-data scenario never depends on xUnit test ordering.
public sealed class AdminDashboardAuthorizationTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Teacher)]
    public async Task NonAdmin_CannotReadDashboardThroughMvcOrService(string role)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminDashboardQueryService>();
        Assert.Null(await service.GetAsync(user.Id));
        Assert.Null(await service.GetAsync(Guid.NewGuid()));
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        using var response = await client.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData(AccountStatus.Locked)]
    [InlineData(AccountStatus.Disabled)]
    public async Task InactiveAdmin_CannotReadDashboard(AccountStatus status)
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.SingleAsync(u => u.Id == admin.Id)).AccountStatus = status;
        await db.SaveChangesAsync();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IAdminDashboardQueryService>().GetAsync(admin.Id));
    }

    [Fact]
    public async Task Guest_MustLogin()
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }
}
