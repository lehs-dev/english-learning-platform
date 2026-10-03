using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> e)
    {
        e.ToTable("Payment");
        e.Property(x => x.Amount).HasPrecision(18,2);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);  
        e.Property(x => x.Provider).HasMaxLength(64).IsRequired();   
        
        
        e.Property(x => x.ProviderTransactionId).HasMaxLength(128).IsRequired();   
        /*chìa khóa chống thanh toán trùng: 
            callback/webhook gọi lặp với cùng mã giao dịch 
                → database từ chối insert 
                → không bao giờ tạo 2 Payment (và 2 Enrollment) cho 1 lần trả tiền.
        */
        e.HasIndex(x => x.ProviderTransactionId).IsUnique();

        e.HasOne(x => x.Order).WithMany(x => x.Payments).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
    }
}
