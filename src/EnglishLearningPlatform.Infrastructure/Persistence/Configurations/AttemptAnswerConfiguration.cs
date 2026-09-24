using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class AttemptAnswerConfiguration : IEntityTypeConfiguration<AttemptAnswer>
{
    public void Configure(EntityTypeBuilder<AttemptAnswer> e)
    {
        e.ToTable("AttemptAnswers");
        e.HasIndex(x => new { x.AttemptId, x.AssessmentQuestionId }).IsUnique();
        e.HasOne(x => x.Attempt).WithMany(x => x.Answers)
            .HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.AssessmentQuestion).WithMany(x => x.Answers)
            .HasForeignKey(x => x.AssessmentQuestionId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.SelectedOption).WithMany()
            .HasForeignKey(x => x.SelectedOptionId).OnDelete(DeleteBehavior.NoAction);
    }
}
