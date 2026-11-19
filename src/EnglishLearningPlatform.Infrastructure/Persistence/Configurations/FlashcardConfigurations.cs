using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class FlashcardDeckConfiguration : IEntityTypeConfiguration<FlashcardDeck>
{
    public void Configure(EntityTypeBuilder<FlashcardDeck> e)
    {
        e.ToTable("FlashcardDecks", t => t.HasCheckConstraint("CK_FlashcardDecks_Status", "[Status] IN (N'Draft', N'Published', N'Archived')"));
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Description).HasMaxLength(4000);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        e.HasIndex(x => new { x.CourseId, x.Status });
        e.HasIndex(x => new { x.OwnerTeacherUserId, x.Status });
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerTeacherUserId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Lesson).WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class FlashcardConfiguration : IEntityTypeConfiguration<Flashcard>
{
    public void Configure(EntityTypeBuilder<Flashcard> e)
    {
        e.ToTable("Flashcards", t => t.HasCheckConstraint("CK_Flashcards_OrderIndex", "[OrderIndex] >= 0"));
        e.Property(x => x.FrontText).HasMaxLength(500).IsRequired();
        e.Property(x => x.BackText).HasMaxLength(1000).IsRequired();
        e.Property(x => x.Example).HasMaxLength(2000);
        e.Property(x => x.Pronunciation).HasMaxLength(200);
        e.Property(x => x.AudioResourceUrl).HasMaxLength(2000);
        e.HasIndex(x => new { x.DeckId, x.OrderIndex }).IsUnique();
        e.HasOne(x => x.Deck).WithMany(x => x.Cards).HasForeignKey(x => x.DeckId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class FlashcardReviewConfiguration : IEntityTypeConfiguration<FlashcardReview>
{
    public void Configure(EntityTypeBuilder<FlashcardReview> e)
    {
        e.ToTable("FlashcardReviews", t => t.HasCheckConstraint("CK_FlashcardReviews_Rating", "[Rating] IN (N'Again', N'Hard', N'Good')"));
        e.Property(x => x.Rating).HasConversion<string>().HasMaxLength(8).IsRequired();
        e.HasIndex(x => x.ReviewRequestId).IsUnique();
        e.HasIndex(x => new { x.StudentUserId, x.ReviewedAtUtc });
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StudentUserId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Flashcard).WithMany().HasForeignKey(x => x.FlashcardId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class FlashcardReviewStateConfiguration : IEntityTypeConfiguration<FlashcardReviewState>
{
    public void Configure(EntityTypeBuilder<FlashcardReviewState> e)
    {
        e.ToTable("FlashcardReviewStates", t =>
        {
            t.HasCheckConstraint("CK_FlashcardReviewStates_Rating", "[LastRating] IN (N'Again', N'Hard', N'Good')");
            t.HasCheckConstraint("CK_FlashcardReviewStates_Repetitions", "[Repetitions] >= 0");
        });
        e.Property(x => x.LastRating).HasConversion<string>().HasMaxLength(8).IsRequired();
        e.HasIndex(x => new { x.StudentUserId, x.FlashcardId }).IsUnique();
        e.HasIndex(x => new { x.StudentUserId, x.NextDueAtUtc });
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StudentUserId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Flashcard).WithMany().HasForeignKey(x => x.FlashcardId).OnDelete(DeleteBehavior.NoAction);
    }
}
