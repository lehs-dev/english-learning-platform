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

public sealed class AdminReadOnlyTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task GlobalCsrfGate_RejectsPostToActionsWithoutVerbConstraint()
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.PostAsync("/", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/Admin/Transactions")]
    [InlineData("/Admin/Users")]
    public async Task Guest_MustLogin(string path)
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Teacher)]
    public async Task NonAdmin_CannotReadAdministrationThroughControllerOrService(string role)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        foreach (var path in new[] { "/Admin/Transactions", "/Admin/Users" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
        }
        using var scope = factory.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IAdminQueryService>();
        Assert.Null(await query.TransactionsAsync(user.Id, null, null, 1));
        Assert.Null(await query.UsersAsync(user.Id, null, null, 1));
    }

    [Fact]
    public async Task Admin_SearchesTransactionsAndUserStatus_ReadOnlyAndHtmlEncoded()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var title = $"audit-{Guid.NewGuid():N} <script>alert('xss')</script>";
        Guid orderId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = new Order { StudentUserId = student.Id, Course = new Course { OwnerTeacherUserId = teacher.Id, Title = title }, Amount = 199000 };
            db.Payments.Add(new Payment { Order = order, Amount = order.Amount, Status = PaymentStatus.Succeeded,
                ProviderTransactionId = $"admin-{Guid.NewGuid():N}", PaidAtUtc = DateTimeOffset.UtcNow });
            (await db.Users.SingleAsync(u => u.Id == student.Id)).AccountStatus = AccountStatus.Locked;
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        using var transactions = await client.GetAsync($"/Admin/Transactions?search={orderId}&status=Succeeded");
        transactions.EnsureSuccessStatusCode();
        Assert.True(transactions.Headers.CacheControl!.NoStore);
        var html = await transactions.Content.ReadAsStringAsync();
        Assert.Contains(student.Email!, html);
        Assert.Contains(orderId.ToString(), html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("IsCorrect", html);
        Assert.DoesNotContain("PasswordHash", html);
        var users = WebUtility.HtmlDecode(await client.GetStringAsync($"/Admin/Users?search={Uri.EscapeDataString(student.Email!)}&status=Locked"));
        Assert.Contains(student.Email!, users);
        Assert.Contains("Đã khóa", users);
        Assert.Contains(AppRoles.Student, users);

        using var verify = factory.Services.CreateScope();
        var persisted = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await persisted.Orders.CountAsync(o => o.Id == orderId));
        Assert.Equal(title, await persisted.Orders.Where(o => o.Id == orderId).Select(o => o.Course.Title).SingleAsync());
        Assert.Equal(PaymentStatus.Succeeded, (await persisted.Payments.SingleAsync(p => p.OrderId == orderId)).Status);
    }

    [Fact]
    public async Task AdminQueries_FilterStatusAndClampPagination()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var title = $"paging-{Guid.NewGuid():N}";
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = title };
        db.Orders.AddRange(Enumerable.Range(0, 21).Select(_ => new Order { Course = course, StudentUserId = student.Id, Amount = 199000 }));
        await db.SaveChangesAsync();
        var query = scope.ServiceProvider.GetRequiredService<IAdminQueryService>();
        var first = await query.TransactionsAsync(admin.Id, title, PaymentStatus.Pending, -10);
        Assert.NotNull(first);
        Assert.Equal(21, first.Total);
        Assert.Equal(20, first.Items.Count);
        Assert.Equal(1, first.Page);
        var last = await query.TransactionsAsync(admin.Id, title, PaymentStatus.Pending, int.MaxValue);
        Assert.Equal(2, last!.Page);
        Assert.Single(last.Items);
        Assert.Empty((await query.TransactionsAsync(admin.Id, title, PaymentStatus.Succeeded, 1))!.Items);
    }

    [Theory]
    [InlineData("/Admin/Transactions?status=99")]
    [InlineData("/Admin/Users?status=99")]
    [InlineData("/Admin/Transactions?status=invalid")]
    public async Task InvalidAdminFilters_AreRejected(string path)
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(AccountStatus.Locked)]
    [InlineData(AccountStatus.Disabled)]
    public async Task InactiveAdmin_IsDeniedOnNextRequestAndDirectServiceCall(AccountStatus status)
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.SingleAsync(u => u.Id == admin.Id)).AccountStatus = status;
        await db.SaveChangesAsync();
        using var response = await client.GetAsync("/Admin/Users");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
        var query = scope.ServiceProvider.GetRequiredService<IAdminQueryService>();
        Assert.Null(await query.UsersAsync(admin.Id, null, null, 1));
        Assert.Null(await query.TransactionsAsync(admin.Id, null, null, 1));
    }
}
