using EnglishLearningPlatform.Application.Homework;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class WritingAssignmentConfiguration : IEntityTypeConfiguration<WritingAssignment>
{
    public void Configure(EntityTypeBuilder<WritingAssignment> e)
    {
        e.ToTable("WritingAssignments", table => table.HasCheckConstraint("CK_WritingAssignments_Publication",
            "([IsDraft] = 0 OR [PublishAtUtc] IS NULL) AND " +
            "([PublishAtUtc] IS NULL OR [DueAtUtc] IS NULL OR [DueAtUtc] > [PublishAtUtc])"));
        e.Property(row => row.Title).IsRequired().HasMaxLength(WritingHomeworkLimits.TitleLength);
        e.Property(row => row.Prompt).IsRequired().HasMaxLength(WritingHomeworkLimits.PromptLength);
        e.HasIndex(row => new { row.CourseId, row.CreatedAtUtc, row.Id }).IsDescending(false, true, false);
        e.HasOne(row => row.Course).WithMany().HasForeignKey(row => row.CourseId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class WritingSubmissionConfiguration : IEntityTypeConfiguration<WritingSubmission>
{
    public void Configure(EntityTypeBuilder<WritingSubmission> e)
    {
        e.ToTable("WritingSubmissions", table => table.HasCheckConstraint("CK_WritingSubmissions_Review",
            "([ReviewedAtUtc] IS NULL AND [Feedback] IS NULL AND [Score] IS NULL) OR " +
            "([ReviewedAtUtc] IS NOT NULL AND [Feedback] IS NOT NULL AND ([Score] IS NULL OR [Score] BETWEEN 0 AND 100))"));
        e.Property(row => row.Response).IsRequired().HasMaxLength(WritingHomeworkLimits.ResponseLength);
        e.Property(row => row.Feedback).HasMaxLength(WritingHomeworkLimits.FeedbackLength);
        e.HasIndex(row => new { row.AssignmentId, row.StudentUserId }).IsUnique();
        e.HasIndex(row => new { row.AssignmentId, row.ReviewedAtUtc, row.CreatedAtUtc, row.Id });
        e.HasOne(row => row.Assignment).WithMany(row => row.Submissions)
            .HasForeignKey(row => row.AssignmentId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(row => row.StudentUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class WritingSubmissionDraftConfiguration : IEntityTypeConfiguration<WritingSubmissionDraft>
{
    public void Configure(EntityTypeBuilder<WritingSubmissionDraft> e)
    {
        e.ToTable("WritingSubmissionDrafts");
        e.HasKey(row => new { row.AssignmentId, row.StudentUserId });
        e.Property(row => row.Response).IsRequired().HasMaxLength(WritingHomeworkLimits.ResponseLength);
        e.HasOne<WritingAssignment>().WithMany().HasForeignKey(row => row.AssignmentId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(row => row.StudentUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
