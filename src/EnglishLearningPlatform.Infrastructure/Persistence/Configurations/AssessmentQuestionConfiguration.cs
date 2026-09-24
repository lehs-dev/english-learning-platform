using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class AssessmentQuestionConfiguration
    : IEntityTypeConfiguration<AssessmentQuestion>
{
    public void Configure(EntityTypeBuilder<AssessmentQuestion> e)
    {
        e.ToTable("AssessmentQuestions", t => t.HasCheckConstraint(
            "CK_AssessmentQuestions_Points", "[Points] > 0"));

        e.Property(x => x.Points).HasPrecision(8, 2);
        e.HasIndex(x => new { x.AssessmentId, x.QuestionId }).IsUnique();
        e.HasIndex(x => new { x.AssessmentId, x.OrderIndex }).IsUnique();
        e.HasOne(x => x.Assessment).WithMany(x => x.Questions)
            .HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Question).WithMany(x => x.AssessmentQuestions)
            .HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.NoAction);
    }
}
