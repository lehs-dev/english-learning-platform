using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> e)
    {
        e.ToTable("Enrollments");
        e.HasIndex(x => new { x.StudentUserId, x.CourseId }).IsUnique();
        e.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.StudentUserId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Course).WithMany(x => x.Enrollments)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.LastAccessedLesson).WithMany()
            .HasForeignKey(x => x.LastAccessedLessonId).OnDelete(DeleteBehavior.NoAction);
    }
}
