using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class CommercePaymentTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task DuplicateProviderTransactionId_IsRejected()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var course = new Course
        {
            OwnerTeacherUserId = teacher.Id,
            Title = "Khóa trả phí demo",
            IsPaid = true,
            Price = 199_000
        };

        var order = new Order
        {
            StudentUserId = student.Id,
            Course = course,
            Amount = 199_000
        };

        db.Payments.Add(new Payment
        {
            Order = order,
            Amount = 199_000,
            ProviderTransactionId = "txn-001",
            Status = PaymentStatus.Succeeded
        });
        await db.SaveChangesAsync();

        //Giả lập callback/webhook bị gợi lặp với cùng mã giao dịch
        db.Payments.Add(new Payment
        {
            OrderId = order.Id,
            Amount = 199_000,
            Provider = "sanbox",
            ProviderTransactionId = "txn-001",
            Status = PaymentStatus.Succeeded
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}