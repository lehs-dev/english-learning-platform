using EnglishLearningPlatform.Application.Gamification;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Gamification;

internal static class StudentActivityDays
{
    public static Task<int> RecordAsync(AppDbContext db, Guid studentId, DateTimeOffset occurredAtUtc, CancellationToken ct)
    {
        var activityDate = GamificationPolicy.LocalDate(occurredAtUtc);
        // The composite key and range lock make concurrent submissions for the same day idempotent.
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [StudentActivityDays] ([StudentUserId], [ActivityDate])
            SELECT {studentId}, {activityDate}
            WHERE NOT EXISTS (
                SELECT 1 FROM [StudentActivityDays] WITH (UPDLOCK, HOLDLOCK)
                WHERE [StudentUserId] = {studentId} AND [ActivityDate] = {activityDate}
            )
            """, ct);
    }
}
