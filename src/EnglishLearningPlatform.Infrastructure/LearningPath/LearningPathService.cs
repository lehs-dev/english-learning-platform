using System.Data;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.LearningPath;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Flashcards;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.LearningPath;

public sealed class LearningPathService(AppDbContext db, TimeProvider time) : ILearningPathService
{
    private static readonly TimeZoneInfo Vietnam = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Vietnam).DateTime);

    public async Task<LearningOutcome<LearningPathPage>> GetActivePlanAsync(Guid studentId, CancellationToken ct = default)
    {
        if (!await db.IsStudentAsync(studentId, ct)) return new(LearningAccessResult.Forbidden);
        var evidence = await RepresentativeAssessmentQueries.ReadAsync(db, studentId, ct);
        var weak = LearningPathPlanner.SelectWeakSkills(evidence);
        var plan = await LatestPlanAsync(studentId, ct);
        return new(LearningAccessResult.Allowed, await PageAsync(studentId, plan, evidence, weak, ct));
    }

    public Task<LearningOutcome<LearningPathPage>> GeneratePlanAsync(Guid studentId, CancellationToken ct = default) =>
        GeneratePlanAsync(studentId, LearningPlan.DefaultDurationDays, ct);

    public async Task<LearningOutcome<LearningPathPage>> GeneratePlanAsync(Guid studentId, int durationDays, CancellationToken ct = default)
    {
        if (!LearningPlan.IsSupportedDuration(durationDays))
            throw new ArgumentOutOfRangeException(nameof(durationDays));
        if (!await db.IsStudentAsync(studentId, ct)) return new(LearningAccessResult.Forbidden);
        var evidence = await RepresentativeAssessmentQueries.ReadAsync(db, studentId, ct);
        var weak = LearningPathPlanner.SelectWeakSkills(evidence);
        // Read course content before taking the plan lock. Completion hooks already hold a course transaction.
        var candidates = await CandidatesAsync(studentId, false, ct);
        var completedLessons = (await db.LessonProgressEntries.AsNoTracking()
            .Where(p => p.Enrollment.StudentUserId == studentId && p.IsCompleted).Select(p => p.LessonId).ToListAsync(ct)).ToHashSet();
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        await LockAsync(studentId, ct);
        var active = await db.Set<LearningPlan>().Include(p => p.Items)
            .SingleOrDefaultAsync(p => p.StudentUserId == studentId && p.Status == LearningPlanStatus.Active, ct);
        var today = Today;
        if (active is not null && !HasExpired(active, today))
        {
            var existing = new LearningPathPage(LearningPathReadiness.Ready, View(active, candidates), today, PlanSkills(active));
            if (tx is not null) await tx.CommitAsync(ct);
            return new(LearningAccessResult.Allowed, existing);
        }
        var skills = weak.Select(s => s.Skill).ToArray();
        var schedulable = candidates.Where(c => c.TargetType != LearningPlanTargetType.Lesson || !completedLessons.Contains(c.TargetId)).ToArray();
        var activities = LearningPathPlanner.Build(skills, schedulable, durationDays);
        if (activities.Count == 0)
        {
            var previous = active ?? await LatestPlanAsync(studentId, ct);
            var fallback = new LearningPathPage(Readiness(evidence, weak),
                previous is null ? null : View(previous, candidates), today, previous is null ? skills : PlanSkills(previous));
            fallback = WithDurationChoices(fallback, schedulable, skills);
            if (fallback.Readiness is LearningPathReadiness.Ready or LearningPathReadiness.InsufficientContent)
                fallback = fallback with { UnavailableRequestedDuration = durationDays };
            if (tx is not null) await tx.CommitAsync(ct);
            return new(LearningAccessResult.Allowed, fallback);
        }
        if (active is not null)
        {
            active.Status = LearningPlanStatus.Archived;
            active.UpdatedAtUtc = time.GetUtcNow();
            // Release the filtered unique active-plan index before inserting its replacement.
            await db.SaveChangesAsync(ct);
        }
        var plan = new LearningPlan
        {
            StudentUserId = studentId,
            SourceAssessmentAttemptId = weak[0].AttemptId,
            StartDate = today,
            DurationDays = durationDays,
            CreatedAtUtc = time.GetUtcNow(),
            Items = activities.Select(a => new LearningPlanItem
            {
                DayIndex = a.DayIndex,
                Position = a.Position,
                TargetType = a.Content.TargetType,
                TargetId = a.Content.TargetId,
                Title = a.Content.Title,
                EstimatedMinutes = a.Content.EstimatedMinutes,
                Skill = a.Content.Skill,
                CreatedAtUtc = time.GetUtcNow()
            }).ToList()
        };
        db.Set<LearningPlan>().Add(plan);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new(LearningAccessResult.Allowed, new(LearningPathReadiness.Ready, View(plan, candidates), today, skills));
    }

    public async Task<LearningAccessResult> SkipItemAsync(Guid studentId, Guid planItemId, CancellationToken ct = default)
    {
        if (!await db.IsStudentAsync(studentId, ct)) return LearningAccessResult.Forbidden;
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        await LockAsync(studentId, ct);
        var item = await db.Set<LearningPlanItem>().Include(i => i.Plan).ThenInclude(p => p.Items)
            .SingleOrDefaultAsync(i => i.Id == planItemId && i.Plan.StudentUserId == studentId, ct);
        if (item is null) return LearningAccessResult.NotFound;
        if (item.Plan.Status != LearningPlanStatus.Active || HasExpired(item.Plan, Today)) return LearningAccessResult.Forbidden;
        if (item.Status is LearningPlanItemStatus.Pending or LearningPlanItemStatus.InProgress or LearningPlanItemStatus.Unavailable)
        {
            item.Status = LearningPlanItemStatus.Skipped;
            item.UpdatedAtUtc = time.GetUtcNow();
            FinishIfDone(item.Plan);
            await db.SaveChangesAsync(ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);
        return LearningAccessResult.Allowed;
    }

    public async Task TrackCompletionAsync(Guid studentId, LearningPlanTargetType type, Guid targetId, CancellationToken ct = default)
    {
        if (!await db.IsStudentAsync(studentId, ct)) return;
        // Never acquire a course/progress lock after the plan lock; the outer course service uses the opposite order.
        if (!await IsAvailableAsync(studentId, type, targetId, ct)) return;
        var today = Today;
        var start = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), Vietnam.GetUtcOffset(today.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
        var completedAt = await VerifiedCompletionAsync(studentId, type, targetId, start, ct);
        if (!completedAt.HasValue) return;
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        await LockAsync(studentId, ct);
        var plan = await db.Set<LearningPlan>().Include(p => p.Items)
            .SingleOrDefaultAsync(p => p.StudentUserId == studentId && p.Status == LearningPlanStatus.Active, ct);
        if (plan is not null && !HasExpired(plan, today))
        {
            int day = today.DayNumber - plan.StartDate.DayNumber + 1;
            var items = plan.Items.Where(i => i.TargetType == type && i.TargetId == targetId && i.DayIndex == day &&
                i.Status is LearningPlanItemStatus.Pending or LearningPlanItemStatus.InProgress or LearningPlanItemStatus.Unavailable).ToArray();
            if (items.Length > 0)
            {
                foreach (var item in items)
                {
                    item.Status = LearningPlanItemStatus.Completed;
                    item.CompletedAtUtc = completedAt;
                    item.UpdatedAtUtc = time.GetUtcNow();
                }
                FinishIfDone(plan);
                await db.SaveChangesAsync(ct);
            }
        }
        if (tx is not null) await tx.CommitAsync(ct);
    }

    private Task<bool> IsAvailableAsync(Guid studentId, LearningPlanTargetType type, Guid targetId, CancellationToken ct) => type switch
    {
        LearningPlanTargetType.Lesson => db.Lessons.AsNoTracking().Effective().AnyAsync(l => l.Id == targetId &&
            l.Module.Course.Status == CourseStatus.Published && db.Enrollments.Valid().Any(e => e.StudentUserId == studentId && e.CourseId == l.Module.CourseId), ct),
        LearningPlanTargetType.Practice => db.AvailablePractices(studentId).AnyAsync(p => p.Id == targetId && (p.LessonId.HasValue
            ? p.Lesson!.Module.Course.Status == CourseStatus.Published : p.Module!.Course.Status == CourseStatus.Published), ct),
        LearningPlanTargetType.FlashcardDeck => db.AvailableFlashcardDecks(studentId).AnyAsync(d => d.Id == targetId, ct),
        _ => Task.FromResult(false)
    };

    private async Task<DateTimeOffset?> VerifiedCompletionAsync(Guid studentId, LearningPlanTargetType type,
        Guid targetId, DateTimeOffset start, CancellationToken ct)
    {
        var end = start.AddDays(1);
        if (type == LearningPlanTargetType.Lesson)
            return await db.LessonProgressEntries.AsNoTracking().Where(p => p.Enrollment.StudentUserId == studentId &&
                p.LessonId == targetId && p.IsCompleted && p.UpdatedAtUtc >= start && p.UpdatedAtUtc < end)
                .Select(p => p.UpdatedAtUtc).MaxAsync(ct);
        if (type == LearningPlanTargetType.Practice)
            return await db.PracticeSubmissions.AsNoTracking().Where(s => s.StudentUserId == studentId &&
                s.PracticeId == targetId && s.SubmittedAtUtc >= start && s.SubmittedAtUtc < end)
                .Select(s => (DateTimeOffset?)s.SubmittedAtUtc).MaxAsync(ct);
        if (type == LearningPlanTargetType.FlashcardDeck)
        {
            var cardCount = await db.Set<Flashcard>().CountAsync(c => c.DeckId == targetId, ct);
            if (cardCount == 0) return null;
            var reviews = db.Set<FlashcardReview>().AsNoTracking().Where(r => r.StudentUserId == studentId &&
                r.Flashcard.DeckId == targetId && r.ReviewedAtUtc >= start && r.ReviewedAtUtc < end);
            if (await reviews.Select(r => r.FlashcardId).Distinct().CountAsync(ct) < Math.Min(5, cardCount)) return null;
            return await reviews.Select(r => (DateTimeOffset?)r.ReviewedAtUtc).MaxAsync(ct);
        }
        return null;
    }

    private void FinishIfDone(LearningPlan plan)
    {
        plan.UpdatedAtUtc = time.GetUtcNow();
        if (plan.Items.All(i => i.Status is LearningPlanItemStatus.Completed or LearningPlanItemStatus.Skipped))
            plan.Status = LearningPlanStatus.Completed;
    }

    private Task<LearningPlan?> LatestPlanAsync(Guid studentId, CancellationToken ct) =>
        db.Set<LearningPlan>().AsNoTracking().Include(p => p.Items)
            .Where(p => p.StudentUserId == studentId && p.Status != LearningPlanStatus.Archived)
            .OrderByDescending(p => p.Status == LearningPlanStatus.Active)
            .ThenByDescending(p => p.StartDate).ThenByDescending(p => p.CreatedAtUtc).ThenBy(p => p.Id).FirstOrDefaultAsync(ct);

    private static bool HasExpired(LearningPlan plan, DateOnly today) =>
        today.DayNumber - plan.StartDate.DayNumber >= plan.DurationDays;

    private static EnglishSkill[] PlanSkills(LearningPlan plan) => plan.Items.Select(i => i.Skill).Distinct().Order().ToArray();

    private async Task<LearningPathPage> PageAsync(Guid studentId, LearningPlan? plan,
        IReadOnlyList<RepresentativeAssessmentResult> evidence, IReadOnlyList<LearningPathWeakSkill> weak, CancellationToken ct)
    {
        var available = await CandidatesAsync(studentId, plan is null, ct);
        var weakSkills = weak.Select(s => s.Skill).ToArray();
        var page = new LearningPathPage(Readiness(evidence, weak), plan is null ? null : View(plan, available),
            Today, plan is null ? weakSkills : PlanSkills(plan));
        if (!page.CanGenerate) return page with { Readiness = LearningPathReadiness.Ready };
        if (page.Readiness != LearningPathReadiness.Ready) return page;
        var schedulable = plan is null ? available : await CandidatesAsync(studentId, true, ct);
        return WithDurationChoices(page, schedulable, weakSkills);
    }

    private static LearningPathPage WithDurationChoices(LearningPathPage page, IReadOnlyList<LearningPathCandidate> candidates,
        IReadOnlyList<EnglishSkill> skills)
    {
        if (page.Readiness != LearningPathReadiness.Ready) return page;
        var durations = LearningPlan.SupportedDurations.Where(days => LearningPathPlanner.Build(skills, candidates, days).Count > 0).ToArray();
        return durations.Length == 0
            ? page with { Readiness = LearningPathReadiness.InsufficientContent }
            : page with { AvailableDurations = durations };
    }

    private static LearningPathReadiness Readiness(IReadOnlyList<RepresentativeAssessmentResult> evidence,
        IReadOnlyList<LearningPathWeakSkill> weak) => !evidence.Any(r => r.Skills.Any(s => s.QuestionCount >= SkillAnalysisRules.MinimumQuestionCount))
            ? LearningPathReadiness.NeedsAssessment : weak.Count == 0 ? LearningPathReadiness.NoWeakSkills : LearningPathReadiness.Ready;

    private static LearningPathPlanView View(LearningPlan plan, IReadOnlyList<LearningPathCandidate> available)
    {
        var keys = available.Select(c => (c.TargetType, c.TargetId)).ToHashSet();
        return new(plan.Id, plan.StartDate, plan.Status, plan.SourceAssessmentAttemptId,
            plan.Items.OrderBy(i => i.DayIndex).ThenBy(i => i.Position).Select(i => new LearningPathItemView(
                i.Id, i.DayIndex, i.TargetType, i.TargetId, i.Title, i.EstimatedMinutes, i.Skill,
                i.Status is LearningPlanItemStatus.Pending or LearningPlanItemStatus.InProgress && !keys.Contains((i.TargetType, i.TargetId))
                    ? LearningPlanItemStatus.Unavailable : i.Status, i.Position, i.CompletedAtUtc)).ToArray())
        {
            DurationDays = plan.DurationDays
        };
    }

    private async Task<List<LearningPathCandidate>> CandidatesAsync(Guid studentId, bool excludeCompletedLessons, CancellationToken ct)
    {
        var enrolled = db.Enrollments.AsNoTracking().Valid().Where(e => e.StudentUserId == studentId && e.Course.Status == CourseStatus.Published);
        var lessons = db.Lessons.AsNoTracking().Effective().Where(l => enrolled.Any(e => e.CourseId == l.Module.CourseId) &&
            (!excludeCompletedLessons || !db.LessonProgressEntries.Any(p => p.Enrollment.StudentUserId == studentId && p.LessonId == l.Id && p.IsCompleted)));
        var lessonRows = await lessons.SelectMany(l => l.Module.Course.Skills.Select(s => new { l.Id, l.Title, s.Skill })).ToListAsync(ct);
        var candidates = lessonRows.Select(l => new LearningPathCandidate(LearningPlanTargetType.Lesson, l.Id, l.Title, l.Skill)).ToList();
        var practices = db.AvailablePractices(studentId).Where(p => p.LessonId.HasValue
            ? p.Lesson!.Module.Course.Status == CourseStatus.Published : p.Module!.Course.Status == CourseStatus.Published);
        var practiceRows = await practices.SelectMany(p => p.Questions.Where(q => q.Version == p.Version)
            .Select(q => new { p.Id, p.Title, Skill = q.Question.PrimarySkill })).Distinct().ToListAsync(ct);
        candidates.AddRange(practiceRows.Select(p => new LearningPathCandidate(LearningPlanTargetType.Practice, p.Id, p.Title, p.Skill)));
        var decks = await db.AvailableFlashcardDecks(studentId)
            .Select(d => new { d.Id, d.Title }).ToListAsync(ct);
        candidates.AddRange(decks.Select(d => new LearningPathCandidate(LearningPlanTargetType.FlashcardDeck, d.Id, d.Title, EnglishSkill.Vocabulary, 5)));
        return candidates;
    }

    private Task LockAsync(Guid studentId, CancellationToken ct)
    {
        var resource = $"ELP.LearningPlan.{studentId:N}";
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 51000, 'Learning plan lock unavailable.', 1;
            """, ct);
    }
}
