using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.AssessmentAttempts;

public sealed class AssessmentQueryService(AppDbContext db, TimeProvider timeProvider) : IAssessmentQueryService
{
    public async Task<AssessmentOverviewModel?> GetAssessmentOverviewAsync(
        Guid studentId,
        Guid assessmentId,
        CancellationToken ct = default)
    {
        if (!await IsActiveStudentAsync(studentId, ct))
            return null;

        var assessment = await AuthorizedAssessments(studentId)
            .Where(a => a.Id == assessmentId && a.Status == AssessmentStatus.Published)
            .Select(a => new
            {
                a.Id, a.CourseId, a.Title, a.Description, a.DurationMinutes, a.MaxAttempts, a.PassingScore, a.Status,
                QuestionCount = a.Questions.Count,
                CourseStatus = a.CourseId.HasValue ? (CourseStatus?)a.Course!.Status : null,
                OwnerAvailable = db.Users.Any(u => u.Id == a.OwnerTeacherUserId && u.AccountStatus != AccountStatus.Disabled)
            })
            .SingleOrDefaultAsync(ct);

        if (assessment is null)
            return null;

        var attempts = await db.Attempts.AsNoTracking()
            .Where(a => a.StudentUserId == studentId && a.AssessmentId == assessmentId)
            .OrderByDescending(a => a.StartedAtUtc).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.Status, a.OverallScore, a.DeadlineUtc, a.StartedAtUtc,
                a.FinalizedAtUtc, a.FinalizationReason, a.Passed })
            .ToListAsync(ct);
        var now = timeProvider.GetUtcNow();
        var active = attempts.SingleOrDefault(a => a.Status == AttemptStatus.InProgress && a.DeadlineUtc > now);

        // Start permits resuming an existing attempt after withdrawal; an
        // unavailable course or independent owner cannot receive a new one.
        if (active is null &&
            (assessment.CourseId.HasValue
                ? assessment.CourseStatus is not (CourseStatus.Published or CourseStatus.Unpublished)
                : !assessment.OwnerAvailable))
            return null;

        var highest = attempts
            .Where(a => a.Status == AttemptStatus.Graded && a.OverallScore.HasValue)
            .Select(a => a.OverallScore)
            .DefaultIfEmpty()
            .Max();

        return new AssessmentOverviewModel(
            assessment.Id, assessment.CourseId, assessment.Title, assessment.Description,
            assessment.DurationMinutes, assessment.MaxAttempts, assessment.PassingScore,
            attempts.Count, highest, active?.Id, active?.DeadlineUtc)
        {
            Status = assessment.Status,
            QuestionCount = assessment.QuestionCount,
            AttemptHistory = attempts.Select(a => new AssessmentAttemptHistoryModel(
                a.Id, a.Status, a.StartedAtUtc, a.DeadlineUtc, a.FinalizedAtUtc, a.OverallScore, a.Passed)
            {
                CanViewResult = a.Status == AttemptStatus.Graded && a.OverallScore.HasValue &&
                    a.FinalizedAtUtc.HasValue && a.FinalizationReason.HasValue
            }).ToList()
        };
    }

    public async Task<IReadOnlyList<AssessmentHistoryView>?> GetAttemptHistoryAsync(
        Guid studentId,
        Guid? assessmentId = null,
        CancellationToken ct = default)
    {
        if (!await IsActiveStudentAsync(studentId, ct))
            return null;

        // Quyền xem lịch sử khác quyền Start: chỉ cần đây là Attempt của chính Student.
        // Không yêu cầu đề còn Published hoặc Enrollment còn hiệu lực, không tự chấm bài.
        var rows = await db.Attempts.AsNoTracking()
            .Where(a => a.StudentUserId == studentId &&
                (!assessmentId.HasValue || a.AssessmentId == assessmentId.Value))
            .OrderByDescending(a => a.StartedAtUtc).ThenByDescending(a => a.Id)
            .Select(a => new
            {
                a.AssessmentId, a.Assessment.Title, AssessmentStatus = a.Assessment.Status,
                a.Id, a.Status, a.StartedAtUtc, a.DeadlineUtc,
                a.FinalizedAtUtc, a.FinalizationReason, a.OverallScore, a.Passed
            }).ToListAsync(ct);

        var history = new List<AssessmentHistoryView>();
        foreach (var group in rows.GroupBy(row => row.AssessmentId))
        {
            var attempts = new List<AssessmentAttemptHistoryModel>();
            foreach (var row in group)
            {
                bool canViewResult = row.Status == AttemptStatus.Graded && row.OverallScore.HasValue &&
                    row.FinalizedAtUtc.HasValue && row.FinalizationReason.HasValue;
                attempts.Add(new AssessmentAttemptHistoryModel(
                    row.Id, row.Status, row.StartedAtUtc, row.DeadlineUtc, row.FinalizedAtUtc,
                    canViewResult ? row.OverallScore : null, canViewResult ? row.Passed : null)
                {
                    CanViewResult = canViewResult
                });
            }

            var assessment = group.First();
            history.Add(new AssessmentHistoryView(
                assessment.AssessmentId, assessment.Title, assessment.AssessmentStatus, attempts));
        }

        // Không gọi SaveChanges: xem/F5 lịch sử không sửa trạng thái, điểm hoặc deadline.
        return history;
    }

    public async Task<AssessmentTakingSheet?> GetTakingSheetAsync(
        Guid studentId,
        Guid attemptId,
        CancellationToken ct = default)
    {
        if (!await IsActiveStudentAsync(studentId, ct))
            return null;

        var now = timeProvider.GetUtcNow();
        var assessments = AuthorizedAssessments(studentId);
        var sheet = await db.Attempts.AsNoTracking()
            .Where(a => a.Id == attemptId && a.StudentUserId == studentId &&
                a.Status == AttemptStatus.InProgress && a.DeadlineUtc > now &&
                assessments.Any(assessment => assessment.Id == a.AssessmentId))
            .Select(a => new AssessmentTakingSheet(
                a.Id, a.AssessmentId, a.Assessment.Title, a.Assessment.Description,
                a.Assessment.DurationMinutes, a.DeadlineUtc,
                a.Assessment.Questions.OrderBy(q => q.OrderIndex).ThenBy(q => q.Id)
                    .Select(q => new AssessmentQuestionTakingModel(
                        q.Id, q.QuestionId, q.Question.Content, q.Points,
                        q.Question.Stimulus == null ? null : q.Question.Stimulus.ContentText,
                        a.Answers.Where(answer => answer.AssessmentQuestionId == q.Id)
                            .Select(answer => (Guid?)answer.SelectedOptionId).SingleOrDefault(),
                        q.Question.Options.OrderBy(option => option.OrderIndex).ThenBy(option => option.Id)
                            .Select(option => new AssessmentOptionTakingModel(option.Id, option.Content, option.OrderIndex))
                            .ToList())
                    {
                        AudioUrl = q.Question.Stimulus != null && q.Question.Stimulus.StimulusType == StimulusType.Audio
                            ? q.Question.Stimulus.ResourceUrl : null
                    })
                    .ToList()))
            .SingleOrDefaultAsync(ct);

        // A slow read must not return a sheet whose deadline elapsed in flight.
        if (sheet is null || sheet.DeadlineUtc <= timeProvider.GetUtcNow()) return null;
        // Chỉ trả audio nội bộ theo hash hoặc HTTP(S) legacy; không trả URL tùy ý như file/javascript.
        return sheet with
        {
            Questions = sheet.Questions.Select(question => question with
            {
                AudioUrl = StimulusAudioResource.TryGetHash(question.AudioUrl, out _) || (Uri.TryCreate(question.AudioUrl, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                    ) ? question.AudioUrl : null
            }).ToList()
        };
    }

    public async Task<AssessmentResultView?> GetAttemptResultAsync(
        Guid studentId,
        Guid attemptId,
        CancellationToken ct = default)
    {
        if (!await IsActiveStudentAsync(studentId, ct))
            return null;

        // Results remain readable by their owner after withdrawal or changes
        // to enrollment. A read never finalizes or repairs an attempt.
        var result = await db.Attempts.AsNoTracking()
            .Where(a => a.Id == attemptId && a.StudentUserId == studentId &&
                a.Status == AttemptStatus.Graded && a.OverallScore.HasValue &&
                a.FinalizedAtUtc.HasValue && a.FinalizationReason.HasValue)
            .Select(a => new AssessmentResultView(
                a.Id, a.AssessmentId, a.Assessment.Title, a.Assessment.CourseId,
                a.OverallScore!.Value, a.Assessment.PassingScore, a.Passed ?? false,
                a.FinalizedAtUtc!.Value, a.FinalizationReason!.Value,
                new List<SkillScoreBreakdown>()))
            .SingleOrDefaultAsync(ct);

        if (result is null)
            return null;

        // Chỉ đọc điểm chi tiết sau khi đã xác nhận đây là kết quả của Student.
        // Đếm QuestionId khác nhau; không dùng số câu đã trả lời để xét đủ dữ liệu.
        var skillRows = await db.AttemptSkillResults.AsNoTracking()
            .Where(s => s.AttemptId == attemptId && s.Attempt.StudentUserId == studentId &&
                s.QuestionCount >= SkillAnalysisRules.MinimumQuestionCount)
            .Select(s => new
            {
                s.Skill, s.Score, s.QuestionCount,
                DistinctCount = s.Attempt.Assessment.Questions
                    .Where(q => q.Question.PrimarySkill == s.Skill)
                    .Select(q => q.QuestionId).Distinct().Count(),
                Maximum = s.Attempt.Assessment.Questions
                    .Where(q => q.Question.PrimarySkill == s.Skill).Sum(q => q.Points),
                Earned = s.Attempt.Assessment.Questions
                    .Where(q => q.Question.PrimarySkill == s.Skill &&
                        s.Attempt.Answers.Any(answer => answer.AssessmentQuestionId == q.Id &&
                            q.Question.Options.Any(option => option.Id == answer.SelectedOptionId && option.IsCorrect)))
                    .Sum(q => q.Points)
            }).ToListAsync(ct);

        // Skill lưu dạng chuỗi trong SQL; sắp xếp trong RAM theo thứ tự enum.
        var breakdowns = new List<SkillScoreBreakdown>();
        foreach (var skill in skillRows.OrderBy(s => s.Skill))
        {
            // Dữ liệu lịch sử thiếu/không khớp không được biến thành kết luận năng lực.
            // Chỉ lọc dữ liệu trả về, tuyệt đối không sửa hoặc xóa SkillResult đã lưu.
            if (skill.DistinctCount < SkillAnalysisRules.MinimumQuestionCount ||
                skill.DistinctCount != skill.QuestionCount || skill.Maximum <= 0m ||
                Math.Round(skill.Earned * 100m / skill.Maximum, 2) != skill.Score)
                continue;

            bool isWeak = SkillAnalysisRules.IsWeak(skill.Earned, skill.Maximum, skill.DistinctCount);
            breakdowns.Add(new SkillScoreBreakdown(skill.Skill, skill.Score, skill.DistinctCount, isWeak));
        }

        // DTO chỉ chứa điểm tổng hợp; không trả Answer, Option hoặc Explanation.
        return result with
        {
            SkillBreakdowns = breakdowns
        };
    }

    public async Task<Guid?> FindExpiredAttemptIdAsync(
        Guid studentId,
        Guid assessmentId,
        Guid? attemptId,
        CancellationToken ct = default)
    {
        if (!await IsActiveStudentAsync(studentId, ct))
            return null;

        var now = timeProvider.GetUtcNow();
        var assessments = AuthorizedAssessments(studentId);
        // Đề/Course có thể đã bị rút, nhưng lượt cũ vẫn cần được chấm.
        // Giữ nguyên kiểm tra Enrollment/thanh toán và chỉ tìm lượt của chính Student.
        return await db.Attempts.AsNoTracking()
            .Where(a => a.StudentUserId == studentId && a.AssessmentId == assessmentId &&
                (!attemptId.HasValue || a.Id == attemptId.Value) &&
                a.Status == AttemptStatus.InProgress && a.DeadlineUtc <= now &&
                assessments.Any(assessment => assessment.Id == a.AssessmentId))
            .Select(a => (Guid?)a.Id)
            .SingleOrDefaultAsync(ct);
    }

    private IQueryable<Assessment> AuthorizedAssessments(Guid studentId) => db.Assessments.AsNoTracking()
        .Where(a => !a.CourseId.HasValue ||
            (a.Course != null && a.Course.OwnerTeacherUserId == a.OwnerTeacherUserId &&
                db.Enrollments.AsNoTracking().Valid().Any(e =>
                    e.StudentUserId == studentId && e.CourseId == a.CourseId)));

    private Task<bool> IsActiveStudentAsync(Guid studentId, CancellationToken ct) => db.Users.AsNoTracking()
        .AnyAsync(user => user.Id == studentId && user.AccountStatus == AccountStatus.Active &&
            db.UserRoles.Count(role => role.UserId == studentId) == 1 &&
            db.UserRoles.Any(userRole => userRole.UserId == studentId &&
                db.Roles.Any(role => role.Id == userRole.RoleId && role.Name == AppRoles.Student)), ct);
}
