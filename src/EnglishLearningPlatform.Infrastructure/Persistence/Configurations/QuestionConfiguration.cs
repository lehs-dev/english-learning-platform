using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> e)
    {
        e.ToTable("Questions", table =>
       {
           table.HasCheckConstraint(
                "CK_Questions_PrimarySkill",
                "[PrimarySkill] IN (N'Reading', N'Listening', N'Vocabulary', N'Grammar')");

           table.HasCheckConstraint(
                "CK_Questions_Status",
                "[Status] IN (N'Draft', N'Published', N'Archived')");
       });
        e.Property(x => x.Content).HasMaxLength(4000).IsRequired();
        e.Property(x => x.PrimarySkill).HasConversion<string>().HasMaxLength(24).IsRequired();
        e.Property(x => x.Level).HasMaxLength(32);
        e.Property(x => x.Difficulty).HasMaxLength(32);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        e.HasIndex(x => new
        {
            x.OwnerTeacherUserId,
            x.Status,
            x.PrimarySkill
        });
        e.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.OwnerTeacherUserId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Stimulus).WithMany(x => x.Questions)
            .HasForeignKey(x => x.StimulusId).OnDelete(DeleteBehavior.NoAction);
    }
}
