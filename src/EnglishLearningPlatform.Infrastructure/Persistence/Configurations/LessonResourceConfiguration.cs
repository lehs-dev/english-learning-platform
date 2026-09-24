using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class LessonResourceConfiguration : IEntityTypeConfiguration<LessonResource>
{
    public void Configure(EntityTypeBuilder<LessonResource> e)
    {
        e.ToTable("LessonResources");
        e.Property(x => x.ResourceType).HasConversion<string>().HasMaxLength(16);
        e.Property(x => x.Title).HasMaxLength(200);
        e.Property(x => x.ResourceUrl).HasMaxLength(2048);
        e.HasIndex(x => new { x.LessonId, x.OrderIndex }).IsUnique();
        e.HasOne(x => x.Lesson).WithMany(x => x.Resources)
            .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.NoAction);
    }
}
