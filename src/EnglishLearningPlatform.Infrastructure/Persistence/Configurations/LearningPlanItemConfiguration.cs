using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class LearningPlanItemConfiguration : IEntityTypeConfiguration<LearningPlanItem>
{
    public void Configure(EntityTypeBuilder<LearningPlanItem> e)
    {
        e.ToTable("LearningPlanItems", t =>
        {
            t.HasCheckConstraint("CK_LearningPlanItems_DayIndex", $"[DayIndex] BETWEEN 1 AND {LearningPlan.MaximumDurationDays}");
            t.HasCheckConstraint("CK_LearningPlanItems_EstimatedMinutes", "[EstimatedMinutes] BETWEEN 1 AND 30");
            t.HasCheckConstraint("CK_LearningPlanItems_Position", "[Position] BETWEEN 1 AND 3");
            t.HasCheckConstraint("CK_LearningPlanItems_Status", "[Status] IN (N'Pending', N'InProgress', N'Completed', N'Skipped', N'Unavailable')");
            t.HasCheckConstraint("CK_LearningPlanItems_TargetType", "[TargetType] IN (N'Course', N'Lesson', N'Practice', N'Assessment', N'FlashcardDeck')");
            t.HasCheckConstraint("CK_LearningPlanItems_Skill", "[Skill] IN (N'Vocabulary', N'Grammar', N'Reading', N'Listening')");
        });
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.TargetType).HasConversion<string>().HasMaxLength(24).IsRequired();
        e.Property(x => x.Skill).HasConversion<string>().HasMaxLength(24).IsRequired();
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        e.HasIndex(x => new { x.PlanId, x.DayIndex, x.Position }).IsUnique();
        e.HasOne(x => x.Plan).WithMany(x => x.Items).HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.NoAction);
    }
}
