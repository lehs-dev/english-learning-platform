using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Infrastructure.Learning;

internal static class LearningQueries
{
    // Free enrollment remains valid if the course is later changed to paid.
    public static IQueryable<Enrollment> Valid(this IQueryable<Enrollment> query) => query.Where(e =>
        !e.PaymentId.HasValue || (e.Payment!.Status == PaymentStatus.Succeeded &&
            e.Payment.Order.StudentUserId == e.StudentUserId && e.Payment.Order.CourseId == e.CourseId &&
            e.Payment.Amount == e.Payment.Order.Amount && e.Payment.Order.Currency == "VND"));

    public static IQueryable<Lesson> Effective(this IQueryable<Lesson> query) => query.Where(l =>
        l.Status == LessonStatus.Published && l.Module.Visibility == ModuleVisibility.Visible);
}
