using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class CourseSkillConfiguration : IEntityTypeConfiguration<CourseSkill>
{
    public void Configure(EntityTypeBuilder<CourseSkill> e)
    {
        e.ToTable("CourseSkills");
        e.HasKey(x => new { x.CourseId, x.Skill });
        e.Property(x => x.Skill).HasConversion<string>().HasMaxLength(24);
        e.HasOne(x => x.Course).WithMany(x => x.Skills)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.NoAction);
    }
}
