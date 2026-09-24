using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> e)
    {
        e.ToTable("Lessons");
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        e.HasIndex(x => new { x.ModuleId, x.OrderIndex }).IsUnique();
        e.HasOne(x => x.Module).WithMany(x => x.Lessons)
            .HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.NoAction);
    }
}
