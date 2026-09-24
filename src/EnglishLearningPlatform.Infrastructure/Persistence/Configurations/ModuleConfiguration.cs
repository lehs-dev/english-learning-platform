using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class ModuleConfiguration : IEntityTypeConfiguration<Module>
{
    public void Configure(EntityTypeBuilder<Module> e)
    {
        e.ToTable("Modules");
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Description).HasMaxLength(4000);
        e.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(16);
        e.HasIndex(x => new { x.CourseId, x.OrderIndex }).IsUnique();
        e.HasOne(x => x.Course).WithMany(x => x.Modules)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.NoAction);
    }
}
