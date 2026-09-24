using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> e)
    {
        e.ToTable("Courses");
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Description).HasMaxLength(4000);
        e.Property(x => x.Objectives).HasMaxLength(4000);
        e.Property(x => x.Level).HasConversion<string>().HasMaxLength(32);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);

        e.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.OwnerTeacherUserId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.FinalAssessment).WithOne()
            .HasForeignKey<Course>(x => x.FinalAssessmentId).OnDelete(DeleteBehavior.NoAction);
    }
}
