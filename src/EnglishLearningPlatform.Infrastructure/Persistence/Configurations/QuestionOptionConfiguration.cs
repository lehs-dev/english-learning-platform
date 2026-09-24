using EnglishLearningPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class QuestionOptionConfiguration : IEntityTypeConfiguration<QuestionOption>
{
    public void Configure(EntityTypeBuilder<QuestionOption> e)
    {
        e.ToTable("QuestionOptions");
        e.Property(x => x.Content).HasMaxLength(1000).IsRequired();
        e.HasIndex(x => new { x.QuestionId, x.OrderIndex }).IsUnique();

        // Tối đa một đáp án đúng; điều kiện có ít nhất một đáp án đúng
        // được kiểm tra khi publish Question.
        e.HasIndex(x => x.QuestionId, "IX_QuestionOptions_OneCorrect")
            .IsUnique().HasFilter("[IsCorrect] = 1");

        e.HasOne(x => x.Question).WithMany(x => x.Options)
            .HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.NoAction);
    }
}
