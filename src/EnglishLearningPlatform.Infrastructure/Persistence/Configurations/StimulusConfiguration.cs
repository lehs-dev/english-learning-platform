using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class StimulusConfiguration : IEntityTypeConfiguration<Stimulus>
{
    public void Configure(EntityTypeBuilder<Stimulus> e)
    {
        e.ToTable("Stimuli");
        e.Property(x => x.StimulusType).HasConversion<string>().HasMaxLength(16);
        e.Property(x => x.Title).HasMaxLength(200);
        e.Property(x => x.ResourceUrl).HasMaxLength(2048);
        e.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.OwnerTeacherUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
