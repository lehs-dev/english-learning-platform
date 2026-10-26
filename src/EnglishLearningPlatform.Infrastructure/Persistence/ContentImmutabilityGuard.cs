using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Persistence;

// Lớp bảo vệ cuối cho các đường ghi EF, kể cả code tương lai không đi qua QuestionAuthoringService.
// Không thay thế authorization; raw SQL/quyền quản trị database vẫn nằm ngoài hàng rào SaveChanges.
internal static class ContentImmutabilityGuard
{
    private static readonly string[] QuestionFields =
        { "Content", "PrimarySkill", "StimulusId", "Explanation", "OwnerTeacherUserId", "ContentLockedAtUtc" };
    private static readonly string[] StimulusFields =
        { "StimulusType", "Title", "ContentText", "ResourceUrl", "OwnerTeacherUserId", "ContentLockedAtUtc" };

    public static bool IsNeeded(AppDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        return db.ChangeTracker.Entries().Any(e =>
            (e.Entity is Question && (e.State == EntityState.Deleted
                || (e.State == EntityState.Modified && QuestionFields.Any(p => e.Property(p).IsModified))))
            || (e.Entity is Stimulus && (e.State == EntityState.Deleted
                || (e.State == EntityState.Modified && StimulusFields.Any(p => e.Property(p).IsModified))))
            || (e.Entity is QuestionOption && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted));
    }

    public static async Task ValidateAsync(AppDbContext db, CancellationToken ct)
    {
        // Đọc dữ liệu thật, không tin OriginalValues hoặc dấu khóa trong object do caller gửi vào.
        foreach (var entry in db.ChangeTracker.Entries<Question>().Where(e => e.State is EntityState.Modified or EntityState.Deleted).OrderBy(e => e.Entity.Id).ToArray())
        {
            if (entry.State != EntityState.Deleted && !QuestionFields.Any(p => entry.Property(p).IsModified)) continue;
            Guid id = entry.Entity.Id;
            var saved = await db.QuestionsForUpdate(id).AsNoTracking().SingleOrDefaultAsync(ct);
            if (saved == null) continue;
            var next = entry.Entity;
            if (saved.ContentLockedAtUtc.HasValue && saved.ContentLockedAtUtc != next.ContentLockedAtUtc)
                throw Locked(); // Không xóa hoặc đổi thời điểm khóa cũ.
            if (await IsQuestionLockedAsync(db, id, ct) && (entry.State == EntityState.Deleted
                || saved.Content != next.Content || saved.PrimarySkill != next.PrimarySkill
                || saved.StimulusId != next.StimulusId || saved.Explanation != next.Explanation
                || saved.OwnerTeacherUserId != next.OwnerTeacherUserId)) throw Locked();
        }

        foreach (var entry in db.ChangeTracker.Entries<QuestionOption>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray())
        {
            var next = entry.Entity;
            var saved = entry.State == EntityState.Added ? null
                : await db.QuestionOptions.AsNoTracking().SingleOrDefaultAsync(o => o.Id == next.Id, ct);
            if (entry.State == EntityState.Modified && saved != null && saved.Content == next.Content
                && saved.IsCorrect == next.IsCorrect && saved.OrderIndex == next.OrderIndex && saved.QuestionId == next.QuestionId) continue;
            foreach (Guid id in new[] { next.QuestionId, saved?.QuestionId ?? next.QuestionId }.Distinct().OrderBy(id => id))
            {
                if (db.ChangeTracker.Entries<Question>().Any(q => q.State == EntityState.Added && q.Entity.Id == id)) continue;
                // Giữ khóa parent tới sau SaveChanges; Publish không chen vào giữa kiểm tra và ghi.
                await db.QuestionsForUpdate(id).AsNoTracking().SingleOrDefaultAsync(ct);
                if (await IsQuestionLockedAsync(db, id, ct)) throw Locked();
            }
        }

        foreach (var entry in db.ChangeTracker.Entries<Stimulus>().Where(e => e.State is EntityState.Modified or EntityState.Deleted).OrderBy(e => e.Entity.Id).ToArray())
        {
            if (entry.State != EntityState.Deleted && !StimulusFields.Any(p => entry.Property(p).IsModified)) continue;
            Guid id = entry.Entity.Id;
            var saved = await db.StimuliForUpdate(id).AsNoTracking().SingleOrDefaultAsync(ct);
            if (saved == null) continue;
            var next = entry.Entity;
            if (saved.ContentLockedAtUtc.HasValue && saved.ContentLockedAtUtc != next.ContentLockedAtUtc) throw Locked();
            bool used = saved.ContentLockedAtUtc.HasValue || await db.Questions.AnyAsync(q => q.StimulusId == id
                && (q.ContentLockedAtUtc != null || q.Status != QuestionStatus.Draft
                    || q.AssessmentQuestions.Any(a => a.Assessment.Status != AssessmentStatus.Draft || a.Assessment.Attempts.Any())
                    || q.PracticeQuestions.Any(p => p.Practice.Status != PracticeStatus.Draft || p.Practice.Submissions.Any())), ct);
            if (used && (entry.State == EntityState.Deleted || saved.ContentText != next.ContentText
                || saved.ResourceUrl != next.ResourceUrl || saved.Title != next.Title
                || saved.StimulusType != next.StimulusType || saved.OwnerTeacherUserId != next.OwnerTeacherUserId)) throw Locked();
        }
    }

    private static Task<bool> IsQuestionLockedAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        db.Questions.AnyAsync(q => q.Id == id && (q.ContentLockedAtUtc != null || q.Status != QuestionStatus.Draft
            || q.AssessmentQuestions.Any(a => a.Assessment.Status != AssessmentStatus.Draft || a.Assessment.Attempts.Any())
            || q.PracticeQuestions.Any(p => p.Practice.Status != PracticeStatus.Draft || p.Practice.Submissions.Any())), ct);

    private static InvalidOperationException Locked() => new(
        "Question/Option/Stimulus đã khóa để bảo toàn lịch sử. Hãy tạo Question/Stimulus mới.");
}
