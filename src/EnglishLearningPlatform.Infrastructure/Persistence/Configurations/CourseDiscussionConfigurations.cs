using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class CourseDiscussionThreadConfiguration : IEntityTypeConfiguration<CourseDiscussionThread>
{
    public void Configure(EntityTypeBuilder<CourseDiscussionThread> e)
    {
        e.Property(x => x.Subject).HasMaxLength(CourseDiscussionLimits.SubjectLength);
        e.HasOne(x => x.Enrollment).WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class CourseDiscussionMessageConfiguration : IEntityTypeConfiguration<CourseDiscussionMessage>
{
    public void Configure(EntityTypeBuilder<CourseDiscussionMessage> e)
    {
        e.Property(x => x.Body).HasMaxLength(CourseDiscussionLimits.BodyLength);
        e.HasOne(x => x.Thread).WithMany(x => x.Messages).HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.NoAction);
        e.HasIndex(x => new { x.ThreadId, x.SequenceNumber }).IsUnique();
    }
}
