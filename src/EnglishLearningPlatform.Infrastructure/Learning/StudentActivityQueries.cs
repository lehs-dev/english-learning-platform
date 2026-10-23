using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

internal static class StudentActivityQueries
{
    public static Task<bool> IsStudentAsync(this AppDbContext db, Guid id, CancellationToken ct) =>
        db.HasActiveRoleAsync(id, AppRoles.Student, ct);

    public static IQueryable<Practice> AvailablePractices(this AppDbContext db, Guid studentId)
    {
        var enrollment = db.Enrollments.Valid().Where(e => e.StudentUserId == studentId &&
            (e.Course.Status == CourseStatus.Published || e.Course.Status == CourseStatus.Unpublished));
        // EXISTS on each parent avoids an OR spanning the two outer-joined course graphs.
        // Both enrollment and owner remain SQL predicates before any metadata is read.
        var lessons = db.Lessons.Effective().Where(l => enrollment.Any(e => e.CourseId == l.Module.CourseId));
        var modules = db.Modules.Where(m => m.Visibility == ModuleVisibility.Visible && enrollment.Any(e => e.CourseId == m.CourseId));
        return db.Practices.AsNoTracking().Where(p => p.Status == PracticeStatus.Published &&
            ((p.ModuleId == null && lessons.Any(l => l.Id == p.LessonId && l.Module.Course.OwnerTeacherUserId == p.OwnerTeacherUserId)) ||
             (p.LessonId == null && modules.Any(m => m.Id == p.ModuleId && m.Course.OwnerTeacherUserId == p.OwnerTeacherUserId))) &&
            p.Questions.Any() && p.Questions.All(q => q.Question.OwnerTeacherUserId == p.OwnerTeacherUserId &&
                q.Question.Options.Count >= 2 && q.Question.Options.Count(o => o.IsCorrect) == 1 &&
                (q.Question.StimulusId == null || q.Question.Stimulus!.OwnerTeacherUserId == p.OwnerTeacherUserId) &&
                (q.Question.PrimarySkill != EnglishSkill.Listening ||
                    (q.Question.Stimulus != null && q.Question.Stimulus.StimulusType == StimulusType.Audio &&
                     q.Question.Stimulus.ResourceUrl != null &&
                     (q.Question.Stimulus.ResourceUrl.StartsWith("https://") || q.Question.Stimulus.ResourceUrl.StartsWith("http://")
                        // Lọc sơ bộ có thể dịch sang SQL. Validator runtime kiểm tra chính xác toàn bộ hash.
                        || (q.Question.Stimulus.ResourceUrl.StartsWith(StimulusAudioConstants.RoutePrefix)
                            && q.Question.Stimulus.ResourceUrl.Length == StimulusAudioConstants.RoutePrefix.Length + StimulusAudioResource.HashLength))))));
    }

    // Withdrawal blocks new starts, but an existing attempt can resume to its deadline.
    public static IQueryable<Assessment> AvailableAssessments(this AppDbContext db, Guid studentId, DateTimeOffset now) =>
        db.Assessments.AsNoTracking().Where(a =>
            (!a.CourseId.HasValue || (a.Course!.OwnerTeacherUserId == a.OwnerTeacherUserId &&
                db.Enrollments.Valid().Any(e => e.StudentUserId == studentId && e.CourseId == a.CourseId))) &&
            ((a.Status == AssessmentStatus.Published && a.Questions.Count >= 5 && a.DurationMinutes > 0 &&
                (a.CourseId.HasValue
                    ? a.Course!.Status == CourseStatus.Published || a.Course.Status == CourseStatus.Unpublished
                    : db.Users.Any(u => u.Id == a.OwnerTeacherUserId && u.AccountStatus != AccountStatus.Disabled))) ||
             a.Attempts.Any(t => t.StudentUserId == studentId && t.Status == AttemptStatus.InProgress && t.DeadlineUtc > now)));
}
