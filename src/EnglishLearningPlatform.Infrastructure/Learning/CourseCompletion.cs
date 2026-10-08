using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

internal static class CourseCompletion
{
    // Caller holds the Course UPDLOCK in its transaction before locking Assessment/Attempt
    // or changing Enrollment/Progress. Never open a second transaction here.
    public static Task<int> ReconcileAsync(AppDbContext db, Guid courseId, CancellationToken ct)
    {
        var effective = db.Lessons.Effective().Where(l => l.Module.CourseId == courseId);
        var now = DateTimeOffset.UtcNow;
        return db.Enrollments.Valid().Where(e => e.CourseId == courseId && e.CompletedAtUtc == null &&
            (e.Course.Status == CourseStatus.Published || e.Course.Status == CourseStatus.Unpublished) &&
            effective.Any() && !effective.Any(l => !db.LessonProgressEntries.Any(p =>
                p.EnrollmentId == e.Id && p.LessonId == l.Id && p.IsCompleted)) &&
            (!e.Course.FinalAssessmentId.HasValue || (e.Course.FinalAssessment!.CourseId == courseId &&
                e.Course.FinalAssessment.OwnerTeacherUserId == e.Course.OwnerTeacherUserId &&
                e.Course.FinalAssessment.Status == AssessmentStatus.Published &&
                (e.Course.FinalAssessment.AssessmentType == AssessmentType.SkillAssessment ||
                 e.Course.FinalAssessment.AssessmentType == AssessmentType.PracticeExam) &&
                e.Course.FinalAssessment.PassingScore != null &&
                e.Course.FinalAssessment.PassingScore >= 0 && e.Course.FinalAssessment.PassingScore <= 100 &&
                db.Attempts.Any(a => a.StudentUserId == e.StudentUserId &&
                    a.AssessmentId == e.Course.FinalAssessmentId && a.Status == AttemptStatus.Graded &&
                    // Passed was decided from raw points. A rounded display score alone
                    // can equal the threshold even when the student actually failed.
                    a.Passed == true && a.OverallScore >= e.Course.FinalAssessment.PassingScore))))
            .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.CompletedAtUtc, (DateTimeOffset?)now), ct);
    }
}
