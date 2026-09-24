using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class CourseTopicConfiguration : IEntityTypeConfiguration<CourseTopic>
{
    public void Configure(EntityTypeBuilder<CourseTopic> e)
    {
        e.ToTable("CourseTopics");
        e.Property(x => x.Topic).HasMaxLength(120).IsRequired();
        e.HasIndex(x => new { x.CourseId, x.Topic }).IsUnique();
        e.HasOne(x => x.Course).WithMany(x => x.Topics)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.NoAction);
    }
}
