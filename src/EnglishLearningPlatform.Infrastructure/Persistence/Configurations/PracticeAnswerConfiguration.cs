using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class PracticeAnswerConfiguration : IEntityTypeConfiguration<PracticeAnswer>
{
    public void Configure(EntityTypeBuilder<PracticeAnswer> e)
    {
        e.ToTable("PracticeAnswers");
        e.HasIndex(x => new { x.PracticeSubmissionId, x.PracticeQuestionId }).IsUnique();
        e.HasOne(x => x.PracticeSubmission).WithMany(x => x.Answers)
            .HasForeignKey(x => x.PracticeSubmissionId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.PracticeQuestion).WithMany(x => x.Answers)
            .HasForeignKey(x => x.PracticeQuestionId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.SelectedOption).WithMany()
            .HasForeignKey(x => x.SelectedOptionId).OnDelete(DeleteBehavior.NoAction);
    }
}
