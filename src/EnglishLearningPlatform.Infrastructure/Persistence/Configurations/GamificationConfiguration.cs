using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class GamificationEventConfiguration : IEntityTypeConfiguration<GamificationEvent>
{
    public void Configure(EntityTypeBuilder<GamificationEvent> e)
    {
        e.ToTable("GamificationEvents", t =>
        {
            t.HasCheckConstraint("CK_GamificationEvents_Xp", "[Xp] > 0");
            t.HasCheckConstraint("CK_GamificationEvents_EventType", "[EventType] IN (N'LessonCompleted', N'PracticeSubmitted', N'PracticeHighScorer', N'FlashcardsReviewed', N'DailyGoalReached')");
        });
        e.Property(x => x.EventType).HasConversion<string>().HasMaxLength(32);
        e.Property(x => x.WindowKey).HasMaxLength(10).IsRequired();
        e.HasIndex(x => new { x.StudentUserId, x.EventType, x.SourceId, x.WindowKey }).IsUnique();
        e.HasIndex(x => new { x.StudentUserId, x.OccurredAtUtc });
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StudentUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class StudentAchievementConfiguration : IEntityTypeConfiguration<StudentAchievement>
{
    public void Configure(EntityTypeBuilder<StudentAchievement> e)
    {
        e.ToTable("StudentAchievements");
        e.Property(x => x.BadgeCode).HasMaxLength(40).IsRequired();
        e.HasIndex(x => new { x.StudentUserId, x.BadgeCode }).IsUnique();
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StudentUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
