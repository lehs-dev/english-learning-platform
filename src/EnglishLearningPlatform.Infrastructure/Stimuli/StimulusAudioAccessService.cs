using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Stimuli;

public sealed class StimulusAudioAccessService(AppDbContext db) : IStimulusAudioAccessService
{
    public async Task<bool> CanReadAsync(Guid actorUserId, string resourceUrl, CancellationToken cancellationToken = default)
    {
        if (!await db.Users.AnyAsync(u => u.Id == actorUserId && u.AccountStatus == AccountStatus.Active, cancellationToken)) return false;
        if (await db.UserRoles.CountAsync(ur => ur.UserId == actorUserId, cancellationToken) != 1) return false;
        bool teacher = await db.UserRoles.AnyAsync(ur => ur.UserId == actorUserId
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Teacher), cancellationToken);
        if (teacher) return await db.Stimuli.AnyAsync(s => s.ResourceUrl == resourceUrl
            && s.OwnerTeacherUserId == actorUserId, cancellationToken);
        bool student = await db.UserRoles.AnyAsync(ur => ur.UserId == actorUserId
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Student), cancellationToken);
        if (!student) return false;

        // IQueryable chỉ dựng SQL, chưa tải danh sách câu hỏi/đáp án về bộ nhớ.
        var questionIds = db.Questions.Where(q => db.Stimuli.Any(s => s.Id == q.StimulusId
            && s.ResourceUrl == resourceUrl)).Select(q => q.Id);
        var assessmentIds = db.AssessmentQuestions.Where(q => questionIds.Contains(q.QuestionId)).Select(q => q.AssessmentId);
        var practiceIds = db.PracticeQuestions.Where(q => questionIds.Contains(q.QuestionId)).Select(q => q.PracticeId);

        var enrolledCourses = db.Enrollments.Valid().Where(e => e.StudentUserId == actorUserId).Select(e => e.CourseId);

        // Rút/Archive không làm mất lịch sử, nhưng có Attempt không thay thế Enrollment.
        // Independent không cần Enrollment; Course-linked phải còn đúng owner và quyền học.
        if (await db.Attempts.AnyAsync(a => a.StudentUserId == actorUserId && assessmentIds.Contains(a.AssessmentId) &&
            (!a.Assessment.CourseId.HasValue ||
             (a.Assessment.Course!.OwnerTeacherUserId == a.Assessment.OwnerTeacherUserId &&
              enrolledCourses.Contains(a.Assessment.CourseId.Value))), cancellationToken)) return true;

        // Practice lịch sử cũng dùng đúng một Lesson/Module thuộc Course đã enroll.
        if (await db.PracticeSubmissions.AnyAsync(p => p.StudentUserId == actorUserId && practiceIds.Contains(p.PracticeId) &&
            ((p.Practice.ModuleId == null && p.Practice.Lesson != null &&
              p.Practice.Lesson.Module.Course.OwnerTeacherUserId == p.Practice.OwnerTeacherUserId &&
              enrolledCourses.Contains(p.Practice.Lesson.Module.CourseId)) ||
             (p.Practice.LessonId == null && p.Practice.Module != null &&
              p.Practice.Module.Course.OwnerTeacherUserId == p.Practice.OwnerTeacherUserId &&
              enrolledCourses.Contains(p.Practice.Module.CourseId))), cancellationToken)) return true;

        // Dùng lại rule của màn hình Student: không viết một bộ Enrollment/Payment/parent khác.
        if (await db.AvailableAssessments(actorUserId, DateTimeOffset.UtcNow)
            .AnyAsync(a => a.Status == AssessmentStatus.Published && assessmentIds.Contains(a.Id), cancellationToken)) return true;
        return await db.AvailablePractices(actorUserId).AnyAsync(p => practiceIds.Contains(p.Id), cancellationToken);
    }
}
