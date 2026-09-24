using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> e)
    {
        e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        e.Property(x => x.AccountStatus).HasConversion<string>().HasMaxLength(16);
        e.HasIndex(x => x.NormalizedEmail).IsUnique();
    }
}
