using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class PracticeSubmissionHistoryConfiguration : IEntityTypeConfiguration<PracticeSubmissionHistory>
{
    public void Configure(EntityTypeBuilder<PracticeSubmissionHistory> e)
    {
        e.ToTable("PracticeSubmissionHistories");
        e.Property(x => x.Score).HasPrecision(5, 2);
        e.HasIndex(x => new { x.StudentUserId, x.PracticeId, x.SubmittedAtUtc });
    }
}
public sealed class PracticeAnswerHistoryConfiguration : IEntityTypeConfiguration<PracticeAnswerHistory>
{
    public void Configure(EntityTypeBuilder<PracticeAnswerHistory> e)
    {
        e.ToTable("PracticeAnswerHistories");
        e.Property(x => x.TextAnswer).HasMaxLength(500);
        e.Property(x => x.QuestionContent).HasMaxLength(4000).IsRequired();
        // A full Matching or Ordering answer can exceed the length of a single option.
        e.Property(x => x.SelectedAnswer).HasMaxLength(4096);
        e.Property(x => x.CorrectAnswersJson).IsRequired();
        e.HasIndex(x => new { x.HistoryId, x.PracticeQuestionId }).IsUnique();
        e.HasOne(x => x.History).WithMany(x => x.Answers).HasForeignKey(x => x.HistoryId).OnDelete(DeleteBehavior.NoAction);
    }
}
