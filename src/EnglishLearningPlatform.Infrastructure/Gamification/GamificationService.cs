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
        var today = GamificationPolicy.LocalDate(time.GetUtcNow());
        var events = db.GamificationEvents.AsNoTracking().Where(e => e.StudentUserId == studentId);
        int xp = await events.SumAsync(e => (int?)e.Xp, ct) ?? 0;
        var dates = await db.StudentActivityDays.AsNoTracking().Where(d => d.StudentUserId == studentId && d.ActivityDate <= today)
            .Select(d => d.ActivityDate).ToListAsync(ct);
        var streaks = GamificationPolicy.StreakProgress(dates, today);
        var badges = await db.StudentAchievements.AsNoTracking().Where(a => a.StudentUserId == studentId)
            .Select(b => new { b.BadgeCode, b.AwardedAtUtc }).ToListAsync(ct);
        var summaries = badges.Select(b =>
            new AchievementSummary(b.BadgeCode, GamificationPolicy.BadgeTitle(b.BadgeCode), b.AwardedAtUtc)).ToList();
        if (streaks.SevenDayMilestone is { } milestone &&
            summaries.All(b => b.Code != GamificationPolicy.SevenDayStreakBadge))
        {
            const string code = GamificationPolicy.SevenDayStreakBadge;
            summaries.Add(new(code, GamificationPolicy.BadgeTitle(code), GamificationPolicy.LocalDayStartUtc(milestone)));
        }
        return new(LearningAccessResult.Allowed, new(xp, streaks.Current, streaks.Longest,
            summaries.OrderBy(b => b.AwardedAtUtc).ThenBy(b => b.Code, StringComparer.Ordinal).ToArray())
        {
            StudiedToday = streaks.StudiedToday,
            RecentActivityDates = dates.Where(d => today.DayNumber - d.DayNumber < 7).Order().ToArray()
        });
    }

    public async Task<bool> AwardXpAsync(Guid studentId, GamificationEventType eventType, Guid sourceId, int xp, CancellationToken ct = default)
    {
        if (sourceId == Guid.Empty || xp <= 0 || xp != GamificationPolicy.XpFor(eventType) ||
            eventType == GamificationEventType.DailyGoalReached || !await users.IsActiveStudentAsync(studentId, ct)) return false;

        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        var now = time.GetUtcNow();
        var startToday = GamificationPolicy.LocalDayStartUtc(GamificationPolicy.LocalDate(now));
        if (!await users.IsActiveStudentAsync(studentId, ct)) return false;
        var evidenceTimestamp = await EvidenceTimestampAsync(studentId, eventType, sourceId, now, startToday, ct);
        if (evidenceTimestamp is not { } earnedAt) return false;
        // Read source evidence before taking the student lock, so two source transactions cannot invert that lock.
        // Serialize rewards and badges per student, including different activities submitted concurrently.
        await db.Users.FromSqlInterpolated($"SELECT * FROM [AspNetUsers] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {studentId}")
            .AsNoTracking().SingleAsync(ct);
        var key = GamificationPolicy.WindowKey(eventType, earnedAt);
        var dailyKey = GamificationPolicy.WindowKey(GamificationEventType.DailyGoalReached, earnedAt);
        var start = GamificationPolicy.LocalDayStartUtc(GamificationPolicy.LocalDate(earnedAt));
        var end = start.AddDays(1);
        if (await db.Set<GamificationEvent>().AnyAsync(e => e.StudentUserId == studentId && e.EventType == eventType &&
                e.SourceId == sourceId && e.WindowKey == key, ct)) return false;
        db.Set<GamificationEvent>().Add(new()
        {
            StudentUserId = studentId,
            EventType = eventType,
            SourceId = sourceId,
            WindowKey = key,
            Xp = xp,
            OccurredAtUtc = earnedAt
        });
        await db.SaveChangesAsync(ct);

        // Two different learning activities complete the daily goal; score bonus is the same activity.
        int activities = await db.Set<GamificationEvent>().Where(e => e.StudentUserId == studentId && e.WindowKey == dailyKey &&
                (e.EventType == GamificationEventType.PracticeSubmitted || e.EventType == GamificationEventType.FlashcardsReviewed) ||
                e.StudentUserId == studentId && e.EventType == GamificationEventType.LessonCompleted && e.OccurredAtUtc >= start && e.OccurredAtUtc < end)
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
                OccurredAtUtc = earnedAt
            });
        var eligible = new List<string>();
        if (await db.Set<GamificationEvent>().AnyAsync(e => e.StudentUserId == studentId && e.EventType == GamificationEventType.LessonCompleted, ct)) eligible.Add("first-lesson");
        if (await db.Set<GamificationEvent>().Where(e => e.StudentUserId == studentId && e.EventType == GamificationEventType.PracticeSubmitted)
            .Select(e => e.SourceId).Distinct().CountAsync(ct) >= 5) eligible.Add("five-practices");
        var existing = await db.Set<StudentAchievement>().Where(a => a.StudentUserId == studentId).Select(a => a.BadgeCode).ToListAsync(ct);
        foreach (var code in eligible.Except(existing))
            db.Set<StudentAchievement>().Add(new() { StudentUserId = studentId, BadgeCode = code, AwardedAtUtc = now });
        await db.SaveChangesAsync(ct);
        await StudentActivityDays.RecordAsync(db, studentId, earnedAt, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return true;
    }

    private async Task<DateTimeOffset?> EvidenceTimestampAsync(Guid student, GamificationEventType type,
        Guid source, DateTimeOffset now, DateTimeOffset start, CancellationToken ct)
    {
        var end = start.AddDays(1);
        return type switch
        {
            GamificationEventType.LessonCompleted => await db.LessonProgressEntries.Where(p => p.LessonId == source && p.IsCompleted &&
                p.Enrollment.StudentUserId == student && db.Enrollments.Valid().Any(e => e.Id == p.EnrollmentId))
                .Select(p => (DateTimeOffset?)(p.UpdatedAtUtc ?? p.CreatedAtUtc)).MaxAsync(ct),
            GamificationEventType.PracticeSubmitted or GamificationEventType.PracticeHighScorer => await db.PracticeSubmissions.Where(s =>
                s.StudentUserId == student && s.PracticeId == source && s.SubmittedAtUtc >= start && s.SubmittedAtUtc < end &&
                (type == GamificationEventType.PracticeSubmitted || s.Score >= 80m))
                .Select(s => (DateTimeOffset?)s.SubmittedAtUtc).MaxAsync(ct),
            GamificationEventType.FlashcardsReviewed => await HasFlashcardEvidenceAsync(student, source, start, end, ct) ? now : null,
            _ => null
        };
    }

    private async Task<bool> HasFlashcardEvidenceAsync(Guid student, Guid deck, DateTimeOffset start, DateTimeOffset end, CancellationToken ct) =>
        await db.Set<FlashcardReview>().Where(r => r.StudentUserId == student && r.Flashcard.DeckId == deck &&
            r.ReviewedAtUtc >= start && r.ReviewedAtUtc < end).Select(r => r.FlashcardId).Distinct().CountAsync(ct) >= 5;
}
