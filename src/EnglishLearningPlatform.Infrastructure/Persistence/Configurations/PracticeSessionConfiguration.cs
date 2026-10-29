using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class PracticeSessionConfiguration : IEntityTypeConfiguration<PracticeSession>
{
    public void Configure(EntityTypeBuilder<PracticeSession> e)
    {
        e.ToTable("PracticeSessions", table => table.HasCheckConstraint("CK_PracticeSessions_Version", "[Version] >= 1"));
        // Mỗi học viên chỉ có một phiên chưa nộp của cùng Practice, kể cả mở nhiều tab.
        e.HasIndex(s => new { s.StudentUserId, s.PracticeId }).IsUnique()
            .HasFilter("[SubmittedAtUtc] IS NULL");
        e.HasOne(s => s.Practice).WithMany().HasForeignKey(s => s.PracticeId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.StudentUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
