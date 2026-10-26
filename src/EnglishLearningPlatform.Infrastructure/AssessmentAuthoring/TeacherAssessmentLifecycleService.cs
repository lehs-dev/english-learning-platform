using System.Data;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishLearningPlatform.Infrastructure.AssessmentAuthoring;

public sealed class TeacherAssessmentLifecycleService : ITeacherAssessmentLifecycleService
{
    private readonly AppDbContext _db;
    private readonly ILogger<TeacherAssessmentLifecycleService> _logger;

    public TeacherAssessmentLifecycleService(AppDbContext db, ILogger<TeacherAssessmentLifecycleService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Chỉ ngừng nhận Attempt mới. Không đổi câu hỏi, điểm hoặc deadline hiện hữu.
    /// Course -> Assessment giữ cùng thứ tự khóa với chọn Final, Start và đổi scope.
    /// </summary>
    public async Task<WriteOutcome> ChangeStatusAsync(Guid actorUserId, Guid assessmentId,
        AssessmentStatus targetStatus, DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!await IsTeacherAsync(actorUserId, cancellationToken))
                return WriteOutcome.Denied(LearningAccessResult.Forbidden);
            var snapshot = await _db.Assessments.AsNoTracking().Where(a =>
                a.Id == assessmentId && a.OwnerTeacherUserId == actorUserId)
                .Select(a => new { a.CourseId }).SingleOrDefaultAsync(cancellationToken);
            if (snapshot == null) return WriteOutcome.Denied(LearningAccessResult.NotFound);
            if (targetStatus != AssessmentStatus.Unpublished && targetStatus != AssessmentStatus.Archived)
                return Failure(assessmentId, "Thao tác chỉ hỗ trợ ngừng xuất bản hoặc lưu trữ Assessment.");

            var courseIds = await _db.Courses.AsNoTracking().Where(c => c.FinalAssessmentId == assessmentId)
                .Select(c => c.Id).ToListAsync(cancellationToken);
            if (snapshot.CourseId.HasValue) courseIds.Add(snapshot.CourseId.Value);

            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var courses = new Dictionary<Guid, Course>();
            foreach (Guid courseId in courseIds.Distinct().OrderBy(id => id))
            {
                var course = await _db.CoursesForUpdate(courseId)
                    .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
                if (course != null) courses.Add(courseId, course);
            }

            var assessment = await _db.AssessmentsForUpdate(assessmentId, actorUserId)
                .SingleOrDefaultAsync(cancellationToken);
            if (assessment == null) return WriteOutcome.Denied(LearningAccessResult.NotFound);
            await _db.Entry(assessment).ReloadAsync(cancellationToken);
            if (!await IsTeacherAsync(actorUserId, cancellationToken))
                return WriteOutcome.Denied(LearningAccessResult.Forbidden);
            if (assessment.OwnerTeacherUserId != actorUserId)
                return WriteOutcome.Denied(LearningAccessResult.NotFound);
            if (assessment.CourseId != snapshot.CourseId || assessment.UpdatedAtUtc != expectedUpdatedAtUtc)
                return Failure(assessmentId, "Đề hoặc phạm vi đã thay đổi. Hãy tải lại trang trước khi thao tác.");
            if (assessment.CourseId.HasValue
                && (!courses.TryGetValue(assessment.CourseId.Value, out var linked)
                    || linked.OwnerTeacherUserId != actorUserId))
                return WriteOutcome.Denied(LearningAccessResult.NotFound);

            var finals = await _db.Courses.AsNoTracking().Where(c => c.FinalAssessmentId == assessmentId)
                .ToListAsync(cancellationToken);
            if (finals.Any(c => c.Status != CourseStatus.Archived))
                return Failure(assessmentId, "Đề đang làm Final của Course chưa Archived. Hãy gỡ/thay Final hợp lệ trước.");
            if (finals.Count > 0 && targetStatus != AssessmentStatus.Archived)
                return Failure(assessmentId, "Final của Course Archived chỉ được lưu trữ, không chuyển sang Unpublished.");

            if (assessment.Status == targetStatus)
            {
                await tx.CommitAsync(cancellationToken);
                return WriteOutcome.Ok(assessmentId);
            }
            bool validSource = assessment.Status == AssessmentStatus.Published
                || (targetStatus == AssessmentStatus.Archived && assessment.Status == AssessmentStatus.Unpublished);
            if (!validSource)
                return Failure(assessmentId, "Chỉ ngừng xuất bản đề Published; chỉ lưu trữ đề Published hoặc Unpublished.");

            var now = DateTimeOffset.UtcNow;
            if (assessment.UpdatedAtUtc.HasValue && now <= assessment.UpdatedAtUtc.Value)
                now = assessment.UpdatedAtUtc.Value.AddTicks(1);
            assessment.Status = targetStatus;
            assessment.UpdatedAtUtc = now;
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return WriteOutcome.Ok(assessmentId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Changing Assessment status failed for {AssessmentId}.", assessmentId);
            return Failure(assessmentId, "Chưa thể đổi trạng thái đề. Hãy tải lại trang và thử lại.");
        }
    }

    // Kiểm tra lại quyền trong transaction; không chỉ tin role của cookie cũ.
    private Task<bool> IsTeacherAsync(Guid id, CancellationToken ct) =>
        _db.HasActiveRoleAsync(id, AppRoles.Teacher, ct);

    private static WriteOutcome Failure(Guid id, params string[] messages) => new(
        LearningAccessResult.Allowed, id, new Dictionary<string, string[]> { [string.Empty] = messages });
}
