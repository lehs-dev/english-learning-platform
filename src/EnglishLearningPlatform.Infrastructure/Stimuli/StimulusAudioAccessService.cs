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

        // Lịch sử của chính Student vẫn được đọc sau khi đề rút/Archive.
        if (await db.Attempts.AnyAsync(a => a.StudentUserId == actorUserId && assessmentIds.Contains(a.AssessmentId), cancellationToken)) return true;
        if (await db.PracticeSubmissions.AnyAsync(p => p.StudentUserId == actorUserId && practiceIds.Contains(p.PracticeId), cancellationToken)) return true;

        // Dùng lại rule của màn hình Student: không viết một bộ Enrollment/Payment/parent khác.
        if (await db.AvailableAssessments(actorUserId, DateTimeOffset.UtcNow)
            .AnyAsync(a => a.Status == AssessmentStatus.Published && assessmentIds.Contains(a.Id), cancellationToken)) return true;
        return await db.AvailablePractices(actorUserId).AnyAsync(p => practiceIds.Contains(p.Id), cancellationToken);
    }
}
