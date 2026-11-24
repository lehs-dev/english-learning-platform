using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class StudentActivityDayConfiguration : IEntityTypeConfiguration<StudentActivityDay>
{
    public void Configure(EntityTypeBuilder<StudentActivityDay> builder)
    {
        builder.ToTable("StudentActivityDays");
        builder.HasKey(day => new { day.StudentUserId, day.ActivityDate });
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(day => day.StudentUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
