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
        var role = await db.ActiveRoleAssignments().Where(user => user.UserId == actorUserId)
            .Select(user => user.Role).SingleOrDefaultAsync(cancellationToken);
        if (role == AppRoles.Teacher) return await db.Stimuli.AnyAsync(s => s.ResourceUrl == resourceUrl
            && s.OwnerTeacherUserId == actorUserId, cancellationToken);
        if (role != AppRoles.Student) return false;

        // IQueryable chỉ dựng SQL, chưa tải danh sách câu hỏi/đáp án về bộ nhớ.
        var questionIds = db.Questions.Where(q => db.Stimuli.Any(s => s.Id == q.StimulusId
            && s.ResourceUrl == resourceUrl)).Select(q => q.Id);
        var assessmentIds = db.AssessmentQuestions.Where(q => questionIds.Contains(q.QuestionId)).Select(q => q.AssessmentId);
        var practiceIds = db.PracticeQuestions.Where(q => q.Version == q.Practice.Version && questionIds.Contains(q.QuestionId)).Select(q => q.PracticeId);
        var availablePractices = db.AvailablePractices(actorUserId);

        // Người đang làm được nghe audio của đúng phiên bản đã mở, không phải mọi phiên bản cũ.
        if (await db.PracticeSessions.AnyAsync(s => s.StudentUserId == actorUserId && s.SubmittedAtUtc == null
            && availablePractices.Any(p => p.Id == s.PracticeId)
            && db.PracticeQuestions.Any(q => q.PracticeId == s.PracticeId && q.Version == s.Version
                && questionIds.Contains(q.QuestionId)), cancellationToken)) return true;

        var enrolledCourses = db.Enrollments.Valid().Where(e => e.StudentUserId == actorUserId).Select(e => e.CourseId);

        // Rút/Archive không làm mất lịch sử, nhưng có Attempt không thay thế Enrollment.
        // Independent không cần Enrollment; Course-linked phải còn đúng owner và quyền học.
        if (await db.Attempts.AnyAsync(a => a.StudentUserId == actorUserId && assessmentIds.Contains(a.AssessmentId) &&
            (!a.Assessment.CourseId.HasValue ||
             (a.Assessment.Course!.OwnerTeacherUserId == a.Assessment.OwnerTeacherUserId &&
              enrolledCourses.Contains(a.Assessment.CourseId.Value))), cancellationToken)) return true;

        // Practice lịch sử cũng dùng đúng một Lesson/Module thuộc Course đã enroll.
        if (await db.PracticeSubmissions.AnyAsync(p => p.StudentUserId == actorUserId
            && db.PracticeQuestions.Any(q => q.PracticeId == p.PracticeId && q.Version == p.Version && questionIds.Contains(q.QuestionId)) &&
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
