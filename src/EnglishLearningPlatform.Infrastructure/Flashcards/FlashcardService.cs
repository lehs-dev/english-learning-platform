using System.Data;
using EnglishLearningPlatform.Application.Flashcards;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Gamification;
using EnglishLearningPlatform.Application.LearningPath;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Flashcards;

public sealed class FlashcardService(AppDbContext db, IActiveUserValidator users, TimeProvider time,
    IGamificationService? gamification = null, ILearningPathService? learningPath = null) : IFlashcardService
{
    public async Task<LearningOutcome<IReadOnlyList<FlashcardDeckSummary>>> GetDecksAsync(Guid studentId, CancellationToken ct = default)
    {
        if (!await users.IsActiveStudentAsync(studentId, ct)) return new(LearningAccessResult.Forbidden);
        var now = time.GetUtcNow();
        var states = db.Set<FlashcardReviewState>();
        var decks = await db.AvailableFlashcardDecks(studentId).OrderBy(d => d.Title).ThenBy(d => d.Id)
            .Select(d => new FlashcardDeckSummary(d.Id, d.CourseId, d.Course.Title, d.LessonId, d.Title, d.Description,
                d.Status, d.Cards.Count, d.Cards.Count(c => !states.Any(s => s.StudentUserId == studentId &&
                    s.FlashcardId == c.Id && s.NextDueAtUtc > now)))).ToListAsync(ct);
        return new(LearningAccessResult.Allowed, decks);
    }

    public async Task<LearningOutcome<FlashcardStudySheet>> GetStudyAsync(Guid studentId, Guid deckId, CancellationToken ct = default)
    {
        if (!await users.IsActiveStudentAsync(studentId, ct)) return new(LearningAccessResult.Forbidden);
        var now = time.GetUtcNow();
        var deck = await db.AvailableFlashcardDecks(studentId).Where(d => d.Id == deckId)
            .Select(d => new { d.Id, d.Title, Count = d.Cards.Count }).SingleOrDefaultAsync(ct);
        if (deck == null) return new(LearningAccessResult.NotFound);
        var states = db.Set<FlashcardReviewState>().Where(s => s.StudentUserId == studentId);
        var due = db.Set<Flashcard>().AsNoTracking().Where(c => c.DeckId == deckId &&
            !states.Any(s => s.FlashcardId == c.Id && s.NextDueAtUtc > now));
        var count = await due.CountAsync(ct);
        var card = await due.OrderBy(c => c.OrderIndex).ThenBy(c => c.Id)
            .Select(c => new FlashcardSheet(c.Id, c.FrontText, c.BackText, c.Example, c.Pronunciation, c.AudioResourceUrl))
            .FirstOrDefaultAsync(ct);
        var nextDue = card == null ? await states.Where(s => s.Flashcard.DeckId == deckId)
            .MinAsync(s => (DateTimeOffset?)s.NextDueAtUtc, ct) : null;
        return new(LearningAccessResult.Allowed, new(deck.Id, deck.Title, deck.Count, count, card, Guid.NewGuid(), nextDue));
    }

    public async Task<FlashcardReviewResult> ReviewAsync(Guid studentId, Guid deckId, Guid flashcardId,
        Guid reviewRequestId, FlashcardRating rating, CancellationToken ct = default)
    {
        if (!await users.IsActiveStudentAsync(studentId, ct)) return new(FlashcardWriteStatus.Forbidden);
        if (reviewRequestId == Guid.Empty || !Enum.IsDefined(rating)) return new(FlashcardWriteStatus.Invalid);
        var courseId = await db.Set<FlashcardDeck>().AsNoTracking().Where(d => d.Id == deckId)
            .Select(d => (Guid?)d.CourseId).SingleOrDefaultAsync(ct);
        if (courseId == null) return new(FlashcardWriteStatus.NotFound);

        // Course-first lock order matches authoring. The request range lock also protects keys reused across decks.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.CoursesForUpdate(courseId.Value).SingleOrDefaultAsync(ct);
        await db.Set<FlashcardDeck>().FromSqlInterpolated(
            $"SELECT * FROM [FlashcardDecks] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {deckId}").SingleOrDefaultAsync(ct);
        if (!await users.IsActiveStudentAsync(studentId, ct)) return new(FlashcardWriteStatus.Forbidden);
        if (!await db.AvailableFlashcardDecks(studentId).AnyAsync(d => d.Id == deckId, ct)) return new(FlashcardWriteStatus.Unavailable);
        if (!await db.Set<Flashcard>().AnyAsync(c => c.Id == flashcardId && c.DeckId == deckId, ct))
            return new(FlashcardWriteStatus.NotFound);
        var review = await db.Set<FlashcardReview>().FromSqlInterpolated(
            $"SELECT * FROM [FlashcardReviews] WITH (UPDLOCK, HOLDLOCK) WHERE [ReviewRequestId] = {reviewRequestId}")
            .SingleOrDefaultAsync(ct);
        var state = await db.Set<FlashcardReviewState>().SingleOrDefaultAsync(s => s.StudentUserId == studentId && s.FlashcardId == flashcardId, ct);
        if (review != null)
        {
            if (review.StudentUserId != studentId || review.FlashcardId != flashcardId || review.Rating != rating)
                return new(FlashcardWriteStatus.Conflict);
            await tx.CommitAsync(ct);
            await tx.DisposeAsync();
            await TrackLearningAsync(studentId, deckId, ct);
            return new(FlashcardWriteStatus.Saved, state?.NextDueAtUtc, true);
        }
        var now = time.GetUtcNow();
        if (state != null && state.NextDueAtUtc > now) return new(FlashcardWriteStatus.Conflict);
        var schedule = FlashcardSchedule.Calculate(now, state?.Repetitions ?? 0, rating);
        if (state == null)
        {
            state = new FlashcardReviewState { StudentUserId = studentId, FlashcardId = flashcardId };
            db.Set<FlashcardReviewState>().Add(state);
        }
        state.LastReviewedAtUtc = now;
        state.NextDueAtUtc = schedule.NextDueAtUtc;
        state.LastRating = rating;
        state.Repetitions = schedule.Repetitions;
        state.UpdatedAtUtc = now;
        db.Set<FlashcardReview>().Add(new FlashcardReview
        {
            StudentUserId = studentId, FlashcardId = flashcardId, ReviewRequestId = reviewRequestId,
            Rating = rating, ReviewedAtUtc = now
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await tx.DisposeAsync();
        await TrackLearningAsync(studentId, deckId, ct);
        return new(FlashcardWriteStatus.Saved, state.NextDueAtUtc);
    }

    private async Task TrackLearningAsync(Guid studentId, Guid deckId, CancellationToken ct)
    {
        if (gamification is not null)
            await gamification.AwardXpAsync(studentId, GamificationEventType.FlashcardsReviewed, deckId, 5, ct);
        if (learningPath is not null)
            await learningPath.TrackCompletionAsync(studentId, LearningPlanTargetType.FlashcardDeck, deckId, ct);
    }
}
