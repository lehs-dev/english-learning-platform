using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class LessonProgressConfiguration : IEntityTypeConfiguration<LessonProgress>
{
    public void Configure(EntityTypeBuilder<LessonProgress> e)
    {
        e.ToTable("LessonProgress");
        e.HasIndex(x => new { x.EnrollmentId, x.LessonId }).IsUnique();
        e.HasOne(x => x.Enrollment).WithMany(x => x.LessonProgressEntries)
            .HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Lesson).WithMany(x => x.ProgressEntries)
            .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.NoAction);
    }
}
