using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.TeacherAnalytics;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.TeacherAnalytics;

public sealed class TeacherAnalyticsQueryService(AppDbContext db, IActiveUserValidator users, TimeProvider time) : ITeacherAnalyticsQueryService
{
    public async Task<LearningOutcome<TeacherAnalyticsPage>> GetAsync(Guid teacherId, TeacherAnalyticsQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await users.IsActiveTeacherAsync(teacherId, ct)) return new(LearningAccessResult.Forbidden);
        var owned = db.Courses.AsNoTracking().Where(c => c.OwnerTeacherUserId == teacherId);
        if (query.CourseId.HasValue && !await owned.AnyAsync(c => c.Id == query.CourseId, ct)) return new(LearningAccessResult.Forbidden);
        var today = Application.Gamification.GamificationPolicy.LocalDate(time.GetUtcNow());
        var firstSupportedDate = new DateOnly(2000, 1, 1);
        if (query.To < firstSupportedDate) query.To = firstSupportedDate;
        if (query.From < firstSupportedDate) query.From = firstSupportedDate;
        query.To = query.To.HasValue && query.To.Value <= today ? query.To : today;
        query.From ??= query.To.Value.AddDays(-29);
        if (query.From < firstSupportedDate) query.From = firstSupportedDate;
        if (query.From > query.To) query.From = query.To;
        if (query.From < query.To.Value.AddDays(-365)) query.From = query.To.Value.AddDays(-365);
        var start = new DateTimeOffset(query.From.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();
        var end = new DateTimeOffset(query.To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();
        var selected = owned.Where(c => !query.CourseId.HasValue || c.Id == query.CourseId);
        var enrollments = db.Enrollments.AsNoTracking().Valid().Where(e => selected.Any(c => c.Id == e.CourseId) && e.EnrolledAtUtc < end);
        var lessons = db.Lessons.AsNoTracking().Effective().Where(l => selected.Any(c => c.Id == l.Module.CourseId));
        var practices = db.PracticeSubmissions.AsNoTracking().Where(s => s.Practice.OwnerTeacherUserId == teacherId &&
            (s.Practice.LessonId.HasValue ? selected.Any(c => c.Id == s.Practice.Lesson!.Module.CourseId) : selected.Any(c => c.Id == s.Practice.Module!.CourseId)) &&
            s.SubmittedAtUtc >= start && s.SubmittedAtUtc < end &&
            enrollments.Any(e => e.StudentUserId == s.StudentUserId && e.CourseId == (s.Practice.LessonId.HasValue ? s.Practice.Lesson!.Module.CourseId : s.Practice.Module!.CourseId)));
        var attempts = db.Attempts.AsNoTracking().Where(a => a.Status == AttemptStatus.Graded && a.Assessment.OwnerTeacherUserId == teacherId &&
            a.Assessment.CourseId.HasValue && selected.Any(c => c.Id == a.Assessment.CourseId) && a.FinalizedAtUtc >= start && a.FinalizedAtUtc < end &&
            enrollments.Any(e => e.StudentUserId == a.StudentUserId && e.CourseId == a.Assessment.CourseId));
        int enrollmentCount = await enrollments.CountAsync(ct);
        int completed = await enrollments.CountAsync(e => e.CompletedAtUtc.HasValue &&
            lessons.Any(l => l.Module.CourseId == e.CourseId) &&
            !lessons.Any(l => l.Module.CourseId == e.CourseId && !e.LessonProgressEntries.Any(p => p.LessonId == l.Id && p.IsCompleted)), ct);
        int studentCount = await enrollments.Select(e => e.StudentUserId).Distinct().CountAsync(ct);
        var skills = await db.AttemptSkillResults.AsNoTracking().Where(s => attempts.Any(a => a.Id == s.AttemptId))
            .GroupBy(s => s.Skill).OrderBy(g => g.Key)
            .Select(g => new AnalyticsSkill(g.Key, g.Average(s => s.Score), g.Count())).ToListAsync(ct);
        var practiceAnswers = db.PracticeAnswers.AsNoTracking().Where(a => a.PracticeQuestion.Question.OwnerTeacherUserId == teacherId && practices.Any(s => s.Id == a.PracticeSubmissionId))
            .Select(a => new { a.PracticeQuestion.QuestionId, a.PracticeQuestion.Question.Content,
                Wrong = !(a.IsCorrect ?? (a.SelectedOption != null && a.SelectedOption.IsCorrect)) });
        var assessmentAnswers = db.AttemptAnswers.AsNoTracking().Where(a => a.AssessmentQuestion.Question.OwnerTeacherUserId == teacherId && attempts.Any(t => t.Id == a.AttemptId))
            .Select(a => new { a.AssessmentQuestion.QuestionId, a.AssessmentQuestion.Question.Content,
                Wrong = !(a.IsCorrect ?? (a.SelectedOption != null && a.SelectedOption.IsCorrect)) });
        var missed = await practiceAnswers.Concat(assessmentAnswers).GroupBy(a => new { a.QuestionId, a.Content })
            .Select(g => new { Id = g.Key.QuestionId, g.Key.Content, WrongCount = g.Count(a => a.Wrong), AnswerCount = g.Count() })
            .Where(q => q.WrongCount > 0).OrderByDescending(q => q.WrongCount).ThenBy(q => q.Id).Take(10)
            .Select(q => new AnalyticsQuestion(q.Id, q.Content, q.WrongCount, q.AnswerCount)).ToListAsync(ct);

        query.Page = Math.Clamp(query.Page, 1, Math.Max(1, (studentCount + TeacherAnalyticsPage.PageSize - 1) / TeacherAnalyticsPage.PageSize));
        var studentIds = enrollments.Select(e => e.StudentUserId).Distinct();
        var rows = await db.Users.AsNoTracking().Where(u => studentIds.Contains(u.Id)).OrderBy(u => u.FullName).ThenBy(u => u.Id)
            .Skip((query.Page - 1) * TeacherAnalyticsPage.PageSize).Take(TeacherAnalyticsPage.PageSize)
            .Select(u => new AnalyticsStudent(u.FullName,
                db.LessonProgressEntries.Count(p => p.IsCompleted && p.Enrollment.StudentUserId == u.Id &&
                    enrollments.Any(e => e.Id == p.EnrollmentId) && lessons.Any(l => l.Id == p.LessonId)),
                lessons.Count(l => enrollments.Any(e => e.StudentUserId == u.Id && e.CourseId == l.Module.CourseId)),
                practices.Where(s => s.StudentUserId == u.Id).Average(s => (decimal?)s.Score),
                attempts.Where(a => a.StudentUserId == u.Id).Average(a => a.OverallScore))).ToListAsync(ct);
        return new(LearningAccessResult.Allowed, new(query,
            await owned.OrderBy(c => c.Title).ThenBy(c => c.Id).Select(c => new AnalyticsCourse(c.Id, c.Title)).ToListAsync(ct),
            studentCount, enrollmentCount, completed, enrollmentCount == 0 ? 0 : decimal.Round(completed * 100m / enrollmentCount, 1),
            await practices.AverageAsync(s => (decimal?)s.Score, ct), await attempts.AverageAsync(a => a.OverallScore, ct),
            skills, missed, rows, studentCount));
    }
}
