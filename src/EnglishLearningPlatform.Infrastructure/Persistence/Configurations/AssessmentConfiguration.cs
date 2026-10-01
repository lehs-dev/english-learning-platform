using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class AssessmentConfiguration : IEntityTypeConfiguration<Assessment>
{
    public void Configure(EntityTypeBuilder<Assessment> e)
    {
        e.ToTable("Assessments", table =>
        {
            table.HasCheckConstraint(
            "CK_Assessments_Type",
            "[AssessmentType] IN " +
            "(N'PlacementTest', N'SkillAssessment', N'PracticeExam')");
            table.HasCheckConstraint(
            "CK_Assessments_PlacementPassingScore",
            "[AssessmentType] <> N'PlacementTest' OR [PassingScore] IS NULL");
            table.HasCheckConstraint(
            "CK_Assessments_Status",
            "[Status] IN " +
            "(N'Draft', N'Published', N'Unpublished', N'Archived')");
            table.HasCheckConstraint(
            "CK_Assessments_DurationMinutes",
            "[DurationMinutes] > 0");
            table.HasCheckConstraint(
            "CK_Assessments_MaxAttempts",
            "[MaxAttempts] >= 1");
            table.HasCheckConstraint(
            "CK_Assessments_PassingScore",
            "[PassingScore] IS NULL OR [PassingScore] BETWEEN 0 AND 100");
            table.HasCheckConstraint(
            "CK_Assessments_TargetSkill",
            "[TargetSkill] IS NULL OR [TargetSkill] IN " +
            "(N'Reading', N'Listening', N'Vocabulary', N'Grammar')");
            table.HasCheckConstraint(
            "CK_Assessments_SkillAssessmentTarget",
            "[AssessmentType] <> N'SkillAssessment' OR " +
            "[TargetSkill] IS NOT NULL");
        });

        e.Property(x => x.Title)
        .HasMaxLength(200)
        .IsRequired();

        e.Property(x => x.Description)
        .HasMaxLength(4000);

        e.Property(x => x.AssessmentType)
        .HasConversion<string>()
        .HasMaxLength(24);

        e.Property(x => x.TargetSkill)
        .HasConversion<string>()
        .HasMaxLength(24);

        e.Property(x => x.Status)
        .HasConversion<string>()
        .HasMaxLength(16);

        e.Property(x => x.PassingScore)
        .HasPrecision(5, 2);

        e.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.OwnerTeacherUserId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne(x => x.Course).WithMany(x => x.Assessments)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.NoAction);
    }
}
