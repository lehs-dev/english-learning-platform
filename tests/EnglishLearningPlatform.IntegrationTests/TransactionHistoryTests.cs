using System.Net;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class TransactionHistoryTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    private sealed record SeedData(ApplicationUser Teacher, ApplicationUser StudentA, ApplicationUser StudentB,
        Course CourseA, Course CourseB);

    private async Task<SeedData> SeedUsersAndCourses()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var studentA = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var studentB = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var courseA = new Course
        {
            OwnerTeacherUserId = teacher.Id,
            Title = "Lịch sử A",
            Status = CourseStatus.Published,
            IsPaid = true,
            Price = 250000
        };
        var courseB = new Course
        {
            OwnerTeacherUserId = teacher.Id,
            Title = "Lịch sử B",
            Status = CourseStatus.Published,
            IsPaid = true,
            Price = 350000
        };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Courses.AddRange(courseA, courseB);
        await db.SaveChangesAsync();
        return new(teacher, studentA, studentB, courseA, courseB);
    }

    [Fact]
    public async Task History_IsStudentOwned_AndOrderDetailsStayPrivate()
    {
        var data = await SeedUsersAndCourses();
        var ownOrder = new Order
        {
            StudentUserId = data.StudentA.Id,
            CourseId = data.CourseA.Id,
            Amount = 240000,
            Currency = "VND",
            Provider = "test",
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2)
        };
        var otherOrder = new Order
        {
            StudentUserId = data.StudentB.Id,
            CourseId = data.CourseB.Id,
            Amount = 340000,
            Currency = "VND",
            Provider = "test",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Orders.AddRange(ownOrder, otherOrder);
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
            var history = await service.HistoryAsync(data.StudentA.Id);
            Assert.Equal(CheckoutCode.Allowed, history.Code);
            Assert.Single(history.Value!.Orders);
            Assert.Equal(ownOrder.Id, history.Value.Orders[0].OrderId);
            Assert.Equal(240000, history.Value.Orders[0].Amount);
            Assert.Equal(CheckoutCode.NotFound,
                (await service.GetOrderAsync(data.StudentA.Id, otherOrder.Id)).Code);
            Assert.Equal(CheckoutCode.Forbidden,
                (await service.HistoryAsync(data.Teacher.Id)).Code);
        }

        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, data.StudentA);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var historyResponse = await client.GetAsync("/Checkout/History");
        var historyHtml = await historyResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var decodedHistoryHtml = WebUtility.HtmlDecode(historyHtml);
        Assert.Contains("Lịch sử A", decodedHistoryHtml);
        Assert.DoesNotContain("Lịch sử B", decodedHistoryHtml);
        using var response = await client.GetAsync($"/Checkout/Order/{otherOrder.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var teacherClient = IdentityTestHelpers.CreateClient(factory);
        using var teacherLogin = await IdentityTestHelpers.LoginAsync(teacherClient, data.Teacher);
        using var teacherHistory = await teacherClient.GetAsync("/Checkout/History");
        Assert.Equal(HttpStatusCode.Redirect, teacherHistory.StatusCode);
        Assert.Contains("/Account/AccessDenied", teacherHistory.Headers.Location?.ToString());

        using var guestClient = IdentityTestHelpers.CreateClient(factory);
        using var guestHistory = await guestClient.GetAsync("/Checkout/History");
        Assert.Equal(HttpStatusCode.Redirect, guestHistory.StatusCode);
    }

    [Fact]
    public async Task History_UsesOrderQuote_ResolvesMultiplePaymentsLikeDetails_AndPaginates()
    {
        var data = await SeedUsersAndCourses();
        var baseTime = DateTimeOffset.UtcNow.AddHours(-2);
        var orders = Enumerable.Range(0, 11).Select(index => new Order
        {
            StudentUserId = data.StudentA.Id,
            CourseId = data.CourseA.Id,
            Amount = 100000 + index,
            Currency = index % 2 == 0 ? "VND" : "USD",
            Provider = "test",
            CreatedAtUtc = baseTime.AddMinutes(index)
        }).ToArray();
        orders[10].Payments.Add(new Payment
        {
            Amount = orders[10].Amount,
            Status = PaymentStatus.Failed,
            Provider = "test",
            ProviderTransactionId = $"failed-{Guid.NewGuid():N}"
        });
        orders[10].Payments.Add(new Payment
        {
            Amount = orders[10].Amount,
            Status = PaymentStatus.Succeeded,
            Provider = "test",
            ProviderTransactionId = $"success-{Guid.NewGuid():N}"
        });
        orders[9].Payments.Add(new Payment
        {
            Amount = orders[9].Amount,
            Status = PaymentStatus.Cancelled,
            Provider = "test",
            ProviderTransactionId = $"cancelled-{Guid.NewGuid():N}"
        });
        orders[8].Payments.Add(new Payment
        {
            Amount = orders[8].Amount,
            Status = PaymentStatus.Failed,
            Provider = "test",
            ProviderTransactionId = $"failed-{Guid.NewGuid():N}"
        });

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Orders.AddRange(orders);
            await db.SaveChangesAsync();
            await db.Courses.Where(c => c.Id == data.CourseA.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(c => c.Price, 999999));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
            var firstPage = await service.HistoryAsync(data.StudentA.Id, 1);
            var secondPage = await service.HistoryAsync(data.StudentA.Id, 2);
            var clampedFirstPage = await service.HistoryAsync(data.StudentA.Id, 0);
            var clampedLastPage = await service.HistoryAsync(data.StudentA.Id, int.MaxValue);

            Assert.Equal(CheckoutCode.Allowed, firstPage.Code);
            Assert.Equal(1, clampedFirstPage.Value!.Page);
            Assert.Equal(2, clampedLastPage.Value!.Page);
            Assert.Equal(11, firstPage.Value!.Total);
            Assert.Equal(10, firstPage.Value.Orders.Count);
            Assert.Single(secondPage.Value!.Orders);
            Assert.Equal(orders[10].Id, firstPage.Value.Orders[0].OrderId);
            Assert.Equal(orders[10].Amount, firstPage.Value.Orders.Single(item => item.OrderId == orders[10].Id).Amount);
            Assert.Equal("USD", firstPage.Value.Orders.Single(item => item.OrderId == orders[9].Id).Currency);
            Assert.Equal(PaymentStatus.Succeeded, firstPage.Value.Orders.Single(item => item.OrderId == orders[10].Id).Status);
            Assert.Equal(PaymentStatus.Cancelled, firstPage.Value.Orders.Single(item => item.OrderId == orders[9].Id).Status);
            Assert.Null(firstPage.Value.Orders.Single(item => item.OrderId == orders[7].Id).Status);

            var details = await service.GetOrderAsync(data.StudentA.Id, orders[10].Id);
            Assert.Equal(firstPage.Value.Orders.Single(item => item.OrderId == orders[10].Id).Status, details.Value!.Status);
        }
    }

    [Fact]
    public async Task History_ReturnsEmptyPage_WithoutCreatingCommerceRows()
    {
        var data = await SeedUsersAndCourses();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
        var history = await service.HistoryAsync(data.StudentA.Id);
        Assert.Equal(CheckoutCode.Allowed, history.Code);
        Assert.Empty(history.Value!.Orders);
        Assert.Equal(0, history.Value.Total);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Orders.CountAsync(o => o.StudentUserId == data.StudentA.Id));
        Assert.Equal(0, await db.Payments.CountAsync(p => p.Order.StudentUserId == data.StudentA.Id));
        Assert.Equal(0, await db.Enrollments.CountAsync(e => e.StudentUserId == data.StudentA.Id));

        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, data.StudentA);
        using var response = await client.GetAsync("/Checkout/History");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bạn chưa có giao dịch nào.", html);
    }
}
