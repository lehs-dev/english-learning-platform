using EnglishLearningPlatform.Application.Gamification;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Gamification;

public sealed class GamificationService(AppDbContext db, IActiveUserValidator users, TimeProvider time) : IGamificationService
{
    public async Task<LearningOutcome<GamificationSummary>> GetSummaryAsync(Guid studentId, CancellationToken ct = default)
    {
        if (!await users.IsActiveStudentAsync(studentId, ct)) return new(LearningAccessResult.Forbidden);
        var events = db.Set<GamificationEvent>().AsNoTracking().Where(e => e.StudentUserId == studentId);
        int xp = await events.SumAsync(e => (int?)e.Xp, ct) ?? 0;
        var dates = await ActivityDatesAsync(studentId, ct);
        var streaks = GamificationPolicy.Streaks(dates, GamificationPolicy.LocalDate(time.GetUtcNow()));
        var badges = await db.Set<StudentAchievement>().AsNoTracking().Where(a => a.StudentUserId == studentId)
            .OrderBy(a => a.AwardedAtUtc).ToListAsync(ct);
        return new(LearningAccessResult.Allowed, new(xp, streaks.Current, streaks.Longest,
            badges.Select(b => new AchievementSummary(b.BadgeCode, GamificationPolicy.BadgeTitle(b.BadgeCode), b.AwardedAtUtc)).ToArray()));
    }

    public async Task<bool> AwardXpAsync(Guid studentId, GamificationEventType eventType, Guid sourceId, int xp, CancellationToken ct = default)
    {
        if (sourceId == Guid.Empty || xp <= 0 || xp != GamificationPolicy.XpFor(eventType) ||
            eventType == GamificationEventType.DailyGoalReached || !await users.IsActiveStudentAsync(studentId, ct)) return false;

        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        var now = time.GetUtcNow();
        var localDate = GamificationPolicy.LocalDate(now);
        var start = new DateTimeOffset(localDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();
        if (!await users.IsActiveStudentAsync(studentId, ct) || !await HasEvidenceAsync(studentId, eventType, sourceId, start, start.AddDays(1), ct)) return false;
        // Read source evidence before taking the student lock, so two source transactions cannot invert that lock.
        // Serialize rewards and badges per student, including different activities submitted concurrently.
        await db.Users.FromSqlInterpolated($"SELECT * FROM [AspNetUsers] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {studentId}")
            .AsNoTracking().SingleAsync(ct);
        var key = GamificationPolicy.WindowKey(eventType, now);
        var dailyKey = GamificationPolicy.WindowKey(GamificationEventType.DailyGoalReached, now);
        if (await db.Set<GamificationEvent>().AnyAsync(e => e.StudentUserId == studentId && e.EventType == eventType &&
                e.SourceId == sourceId && e.WindowKey == key, ct)) return false;
        db.Set<GamificationEvent>().Add(new()
        {
            StudentUserId = studentId,
            EventType = eventType,
            SourceId = sourceId,
            WindowKey = key,
            Xp = xp,
            OccurredAtUtc = now
        });
        await db.SaveChangesAsync(ct);

        // Two different learning activities complete the daily goal; score bonus is the same activity.
        int activities = await db.Set<GamificationEvent>().Where(e => e.StudentUserId == studentId && e.WindowKey == dailyKey &&
                (e.EventType == GamificationEventType.PracticeSubmitted || e.EventType == GamificationEventType.FlashcardsReviewed) ||
                e.StudentUserId == studentId && e.EventType == GamificationEventType.LessonCompleted && e.OccurredAtUtc >= start && e.OccurredAtUtc < start.AddDays(1))
            .Select(e => e.SourceId).Distinct().CountAsync(ct);
        if (activities >= 2 && !await db.Set<GamificationEvent>().AnyAsync(e => e.StudentUserId == studentId &&
                e.EventType == GamificationEventType.DailyGoalReached && e.WindowKey == dailyKey, ct))
            db.Set<GamificationEvent>().Add(new()
            {
                StudentUserId = studentId,
                EventType = GamificationEventType.DailyGoalReached,
                SourceId = studentId,
                WindowKey = dailyKey,
                Xp = GamificationPolicy.XpFor(GamificationEventType.DailyGoalReached),
                OccurredAtUtc = now
            });
        var eligible = new List<string>();
        if (await db.Set<GamificationEvent>().AnyAsync(e => e.StudentUserId == studentId && e.EventType == GamificationEventType.LessonCompleted, ct)) eligible.Add("first-lesson");
        if (await db.Set<GamificationEvent>().Where(e => e.StudentUserId == studentId && e.EventType == GamificationEventType.PracticeSubmitted)
            .Select(e => e.SourceId).Distinct().CountAsync(ct) >= 5) eligible.Add("five-practices");
        if (GamificationPolicy.Streaks(await ActivityDatesAsync(studentId, ct), localDate).Longest >= 7) eligible.Add("seven-day-streak");
        var existing = await db.Set<StudentAchievement>().Where(a => a.StudentUserId == studentId).Select(a => a.BadgeCode).ToListAsync(ct);
        foreach (var code in eligible.Except(existing))
            db.Set<StudentAchievement>().Add(new() { StudentUserId = studentId, BadgeCode = code, AwardedAtUtc = now });
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return true;
    }

    private async Task<List<DateOnly>> ActivityDatesAsync(Guid studentId, CancellationToken ct)
    {
        // Vietnam has a fixed UTC+7 offset; reduce to distinct calendar days in SQL.
        var dates = await db.Set<GamificationEvent>().AsNoTracking().Where(e => e.StudentUserId == studentId)
            .Select(e => e.OccurredAtUtc.AddHours(7).Date).Distinct().ToListAsync(ct);
        return dates.Select(DateOnly.FromDateTime).ToList();
    }

    private Task<bool> HasEvidenceAsync(Guid student, GamificationEventType type, Guid source, DateTimeOffset start, DateTimeOffset end, CancellationToken ct) => type switch
    {
        GamificationEventType.LessonCompleted => db.LessonProgressEntries.AnyAsync(p => p.LessonId == source && p.IsCompleted &&
            p.Enrollment.StudentUserId == student && db.Enrollments.Valid().Any(e => e.Id == p.EnrollmentId), ct),
        GamificationEventType.PracticeSubmitted or GamificationEventType.PracticeHighScorer => db.PracticeSubmissions.AnyAsync(s =>
            s.StudentUserId == student && s.PracticeId == source && s.SubmittedAtUtc >= start && s.SubmittedAtUtc < end &&
            (type == GamificationEventType.PracticeSubmitted || s.Score >= 80m), ct),
        GamificationEventType.FlashcardsReviewed => HasFlashcardEvidenceAsync(student, source, start, end, ct),
        _ => Task.FromResult(false)
    };

    private async Task<bool> HasFlashcardEvidenceAsync(Guid student, Guid deck, DateTimeOffset start, DateTimeOffset end, CancellationToken ct) =>
        await db.Set<FlashcardReview>().Where(r => r.StudentUserId == student && r.Flashcard.DeckId == deck &&
            r.ReviewedAtUtc >= start && r.ReviewedAtUtc < end).Select(r => r.FlashcardId).Distinct().CountAsync(ct) >= 5;
}
