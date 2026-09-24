using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> e)
    {
        e.ToTable("AuditLogs");
        e.Property(x => x.ActorSnapshot).HasMaxLength(500).IsRequired();
        e.Property(x => x.TargetSnapshot).HasMaxLength(500);
        e.Property(x => x.Action).HasMaxLength(120).IsRequired();

        e.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
