using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class QuestionAcceptedAnswerConfiguration : IEntityTypeConfiguration<QuestionAcceptedAnswer>
{
    public void Configure(EntityTypeBuilder<QuestionAcceptedAnswer> e)
    {
        e.ToTable("QuestionAcceptedAnswers");
        e.Property(x => x.AnswerText).HasMaxLength(500).IsRequired();
        e.HasIndex(x => new { x.QuestionId, x.OrderIndex }).IsUnique();
        e.HasOne(x => x.Question).WithMany(x => x.AcceptedAnswers).HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.NoAction);
    }
}
