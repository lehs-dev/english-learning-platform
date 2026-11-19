using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Flashcards;

internal static class FlashcardAvailabilityQueries
{
    public static IQueryable<FlashcardDeck> AvailableFlashcardDecks(this AppDbContext db, Guid studentId)
    {
        var enrollments = db.Enrollments.Valid().Where(e => e.StudentUserId == studentId);
        return db.Set<FlashcardDeck>().AsNoTracking().Where(d =>
            d.Status == FlashcardDeckStatus.Published && d.Course.Status == CourseStatus.Published &&
            d.OwnerTeacherUserId == d.Course.OwnerTeacherUserId && enrollments.Any(e => e.CourseId == d.CourseId) &&
            (!d.LessonId.HasValue || (d.Lesson!.Module.CourseId == d.CourseId &&
                d.Lesson.Status == LessonStatus.Published && d.Lesson.Module.Visibility == ModuleVisibility.Visible)) && d.Cards.Any());
    }
}
