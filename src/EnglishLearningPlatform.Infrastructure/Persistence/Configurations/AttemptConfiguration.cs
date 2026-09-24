using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class AttemptConfiguration : IEntityTypeConfiguration<Attempt>
{
    public void Configure(EntityTypeBuilder<Attempt> e)
    {
        e.ToTable("Attempts", t => t.HasCheckConstraint(
            "CK_Attempts_OverallScore",
            "[OverallScore] IS NULL OR [OverallScore] BETWEEN 0 AND 100"));

        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        e.Property(x => x.FinalizationReason).HasConversion<string>().HasMaxLength(24);
        e.Property(x => x.OverallScore).HasPrecision(5, 2);
        e.HasIndex(x => new { x.StudentUserId, x.AssessmentId })
            .IsUnique().HasFilter("[Status] = N'InProgress'");

        e.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.StudentUserId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Assessment).WithMany(x => x.Attempts)
            .HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.NoAction);
    }
}
