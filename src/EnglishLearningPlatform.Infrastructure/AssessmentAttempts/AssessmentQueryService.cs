using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
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
                a.Id, a.CourseId, a.Title, a.Description, a.DurationMinutes, a.MaxAttempts, a.PassingScore,
                CourseStatus = a.CourseId.HasValue ? (CourseStatus?)a.Course!.Status : null,
                OwnerAvailable = db.Users.Any(u => u.Id == a.OwnerTeacherUserId && u.AccountStatus != AccountStatus.Disabled)
            })
            .SingleOrDefaultAsync(ct);

        if (assessment is null)
            return null;

        var attempts = await db.Attempts.AsNoTracking()
            .Where(a => a.StudentUserId == studentId && a.AssessmentId == assessmentId)
            .Select(a => new { a.Id, a.Status, a.OverallScore, a.DeadlineUtc })
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
            attempts.Count, highest, active?.Id, active?.DeadlineUtc);
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
                            .ToList()))
                    .ToList()))
            .SingleOrDefaultAsync(ct);

        // A slow read must not return a sheet whose deadline elapsed in flight.
        return sheet is not null && sheet.DeadlineUtc > timeProvider.GetUtcNow() ? sheet : null;
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
                a.SkillResults
                    .Select(s => new SkillScoreBreakdown(s.Skill, s.Score, s.QuestionCount)).ToList()))
            .SingleOrDefaultAsync(ct);

        // Skill is stored as a string, so SQL ordering would use its name
        // rather than the domain enum order.
        return result is null ? null : result with
        {
            SkillBreakdowns = result.SkillBreakdowns.OrderBy(s => s.Skill).ToList()
        };
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
