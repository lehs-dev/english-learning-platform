using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class PracticeConfiguration : IEntityTypeConfiguration<Practice>
{
    public void Configure(EntityTypeBuilder<Practice> e)
    {
        e.ToTable("Practices", t => t.HasCheckConstraint(
            "CK_Practices_Parent",
            "([LessonId] IS NULL OR [ModuleId] IS NULL) AND " +
            "([Status] <> N'Published' OR " +
            "([LessonId] IS NOT NULL AND [ModuleId] IS NULL) OR " +
            "([LessonId] IS NULL AND [ModuleId] IS NOT NULL))"));

        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Description).HasMaxLength(4000);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);

        e.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.OwnerTeacherUserId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Lesson).WithMany(x => x.Practices)
            .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Module).WithMany(x => x.Practices)
            .HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.NoAction);
    }
}
