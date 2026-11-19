using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class LearningPlanConfiguration : IEntityTypeConfiguration<LearningPlan>
{
    public void Configure(EntityTypeBuilder<LearningPlan> e)
    {
        e.ToTable("LearningPlans", t =>
        {
            t.HasCheckConstraint("CK_LearningPlans_Status", "[Status] IN (N'Active', N'Completed', N'Archived')");
            t.HasCheckConstraint("CK_LearningPlans_DurationDays", $"[DurationDays] IN ({string.Join(", ", LearningPlan.SupportedDurations)})");
        });
        e.Property(x => x.DurationDays).HasDefaultValue(LearningPlan.DefaultDurationDays).HasSentinel(LearningPlan.DefaultDurationDays);
        e.Property(x => x.TimezoneId).HasMaxLength(64).IsRequired();
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        e.HasIndex(x => x.StudentUserId).IsUnique().HasFilter("[Status] = N'Active'");
        e.HasIndex(x => new { x.StudentUserId, x.StartDate });
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StudentUserId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.SourceAssessmentAttempt).WithMany().HasForeignKey(x => x.SourceAssessmentAttemptId).OnDelete(DeleteBehavior.NoAction);
    }
}
