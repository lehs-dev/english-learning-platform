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

public sealed class AdminDateFilterTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task AllLists_FilterVietnamCalendarDay_BeforeCountingAndPaging()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var query = scope.ServiceProvider.GetRequiredService<IAdminQueryService>();
        var marker = $"date-{Guid.NewGuid():N}";
        var start = new DateTimeOffset(2026, 10, 22, 17, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(1);
        var times = new[] { start.AddTicks(-1), start, end.AddTicks(-1), end };
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = marker };
        foreach (var time in times)
        {
            db.Orders.Add(new Order { Course = course, StudentUserId = student.Id, CreatedAtUtc = time });
            db.AuditLogs.Add(new AuditLog { ActorSnapshot = marker, Action = "UpdateUser", OccurredAtUtc = time });
        }
        var account = await db.Users.SingleAsync(u => u.Id == student.Id);
        account.CreatedAtUtc = end.AddTicks(-1);
        await db.SaveChangesAsync();
        var dates = new AdminDateRange(new(2026, 10, 23), new(2026, 10, 23));
        var orders = await query.TransactionsAsync(admin.Id, marker, PaymentStatus.Pending, 99, dates: dates);
        var logs = await query.AuditAsync(admin.Id, marker, 99, dates: dates);
        var users = await query.UsersAsync(admin.Id, student.Email, AccountStatus.Active, 1, role: AppRoles.Student, dates: dates);
        Assert.Equal(2, orders!.Total);
        Assert.Equal(1, orders.Page);
        Assert.Equal(2, logs!.Total);
        Assert.Equal(1, users!.Total);
        Assert.Equal(3, (await query.TransactionsAsync(admin.Id, marker, null, 1, dates: new(null, new(2026, 10, 23))))!.Total);
        Assert.Equal(3, (await query.AuditAsync(admin.Id, marker, 1, dates: new(new(2026, 10, 23), null)))!.Total);
        Assert.Empty((await query.UsersAsync(admin.Id, student.Email, null, 1, dates: new(new(2026, 10, 24), null)))!.Items);
        Assert.Null(await query.AuditAsync(student.Id, marker, 1, dates: dates));
    }

    [Theory]
    [InlineData("Users")]
    [InlineData("Transactions")]
    [InlineData("Audit")]
    public async Task Pages_KeepDatesInPagination_AndExplainInvalidRanges(string action)
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var marker = $"dates-page-{Guid.NewGuid():N}";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = new Course { Title = marker, OwnerTeacherUserId = teacher.Id };
            var time = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
            for (var i = 0; i < 21; i++)
            {
                db.Orders.Add(new Order { Course = course, StudentUserId = admin.Id, CreatedAtUtc = time });
                db.AuditLogs.Add(new AuditLog { ActorSnapshot = marker, Action = "UpdateUser", OccurredAtUtc = time });
                db.Users.Add(new ApplicationUser { FullName = marker, UserName = $"{marker}-{i}", CreatedAtUtc = time });
            }
            await db.SaveChangesAsync();
        }
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        var html = await client.GetStringAsync($"/Admin/{action}?search={marker}&fromDate=2026-01-01&toDate=2026-12-31");
        Assert.Contains("type=\"date\"", html);
        Assert.Contains("value=\"2026-01-01\"", html);
        Assert.Contains("value=\"2026-12-31\"", html);
        Assert.Contains("fromDate=2026-01-01&amp;toDate=2026-12-31", html);
        Assert.Contains("page=2", html);
        foreach (var input in new[] { "fromDate=2026-10-24&toDate=2026-10-23", "fromDate=bad", "toDate=9999-12-31", "fromDate=0001-01-01" })
        {
            using var response = await client.GetAsync($"/Admin/{action}?{input}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
        }
    }
}
