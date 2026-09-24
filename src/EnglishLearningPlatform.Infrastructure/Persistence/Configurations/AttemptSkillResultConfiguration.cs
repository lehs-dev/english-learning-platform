using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class AttemptSkillResultConfiguration
    : IEntityTypeConfiguration<AttemptSkillResult>
{
    public void Configure(EntityTypeBuilder<AttemptSkillResult> e)
    {
        e.ToTable("AttemptSkillResults", t =>
        {
            t.HasCheckConstraint("CK_AttemptSkillResults_Score", "[Score] BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_AttemptSkillResults_Count", "[QuestionCount] >= 0");
        });

        e.Property(x => x.Skill).HasConversion<string>().HasMaxLength(24);
        e.Property(x => x.Score).HasPrecision(5, 2);
        e.HasIndex(x => new { x.AttemptId, x.Skill }).IsUnique();
        e.HasOne(x => x.Attempt).WithMany(x => x.SkillResults)
            .HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.NoAction);
    }
}
