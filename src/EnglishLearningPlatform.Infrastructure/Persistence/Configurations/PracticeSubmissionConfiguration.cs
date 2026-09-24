using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class PracticeSubmissionConfiguration
    : IEntityTypeConfiguration<PracticeSubmission>
{
    public void Configure(EntityTypeBuilder<PracticeSubmission> e)
    {
        e.ToTable("PracticeSubmissions", t => t.HasCheckConstraint(
            "CK_PracticeSubmissions_Score", "[Score] BETWEEN 0 AND 100"));

        e.Property(x => x.Score).HasPrecision(5, 2);
        e.HasIndex(x => new { x.StudentUserId, x.PracticeId }).IsUnique();
        e.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.StudentUserId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Practice).WithMany(x => x.Submissions)
            .HasForeignKey(x => x.PracticeId).OnDelete(DeleteBehavior.NoAction);
    }
}
