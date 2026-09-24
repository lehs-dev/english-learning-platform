using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class PracticeQuestionConfiguration : IEntityTypeConfiguration<PracticeQuestion>
{
    public void Configure(EntityTypeBuilder<PracticeQuestion> e)
    {
        e.ToTable("PracticeQuestions");
        e.HasIndex(x => new { x.PracticeId, x.QuestionId }).IsUnique();
        e.HasIndex(x => new { x.PracticeId, x.OrderIndex }).IsUnique();
        e.HasOne(x => x.Practice).WithMany(x => x.Questions)
            .HasForeignKey(x => x.PracticeId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Question).WithMany(x => x.PracticeQuestions)
            .HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.NoAction);
    }
}
