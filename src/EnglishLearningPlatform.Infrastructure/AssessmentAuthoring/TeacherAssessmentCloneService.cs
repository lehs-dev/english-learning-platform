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

public sealed class TeacherAssessmentCloneService(
    AppDbContext db, ILogger<TeacherAssessmentCloneService> logger) : ITeacherAssessmentCloneService
{
    /// <summary>
    /// Sao chép định nghĩa, không sao chép lịch sử hoặc sửa Course.FinalAssessmentId.
    /// Khóa Course -> Assessment như Start và sửa scope để đọc một phiên bản nhất quán.
    /// </summary>
    public async Task<WriteOutcome> CloneAsync(Guid actorUserId, Guid sourceId,
        DateTimeOffset? expectedUpdatedAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!await IsTeacherAsync(actorUserId, cancellationToken))
                return WriteOutcome.Denied(LearningAccessResult.Forbidden);
            var snapshot = await db.Assessments.AsNoTracking()
                .Where(a => a.Id == sourceId && a.OwnerTeacherUserId == actorUserId)
                .Select(a => new { a.CourseId }).SingleOrDefaultAsync(cancellationToken);
            if (snapshot == null) return WriteOutcome.Denied(LearningAccessResult.NotFound);

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (snapshot.CourseId.HasValue)
            {
                var course = await db.CoursesForUpdate(snapshot.CourseId.Value)
                    .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
                if (course == null || course.OwnerTeacherUserId != actorUserId)
                    return WriteOutcome.Denied(LearningAccessResult.NotFound);
            }
            var source = await db.AssessmentsForUpdate(sourceId, actorUserId)
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (source == null) return WriteOutcome.Denied(LearningAccessResult.NotFound);
            if (!await IsTeacherAsync(actorUserId, cancellationToken))
                return WriteOutcome.Denied(LearningAccessResult.Forbidden);
            if (source.CourseId != snapshot.CourseId || source.UpdatedAtUtc != expectedUpdatedAtUtc)
                return Failure(sourceId, "Đề đã thay đổi. Hãy tải lại danh sách trước khi Clone.");

            var items = await db.AssessmentQuestions.AsNoTracking()
                .Where(q => q.AssessmentId == sourceId).OrderBy(q => q.OrderIndex).ThenBy(q => q.Id)
                .ToListAsync(cancellationToken);
            // Không đưa câu hỏi của người khác vào clone nếu dữ liệu cũ bị sai owner.
            var ids = items.Select(q => q.QuestionId).ToArray();
            if (await db.Questions.AnyAsync(q => ids.Contains(q.Id) && (q.OwnerTeacherUserId != actorUserId
                || (q.StimulusId != null && q.Stimulus!.OwnerTeacherUserId != actorUserId)), cancellationToken))
                return Failure(sourceId, "Đề có câu hỏi không cùng owner; chưa thể Clone.");

            const string copyLabel = "(bản sao)";
            const string suffix = " " + copyLabel;
            string title = source.Title.TrimEnd();
            // Chỉ bỏ nhãn ở cuối tên, kể cả tên cũ đã bị lặp nhiều lần.
            // Không xóa cụm "(bản sao)" nằm giữa nội dung tên do Teacher đặt.
            while (title.EndsWith(copyLabel, StringComparison.OrdinalIgnoreCase))
            {
                title = title.Substring(0, title.Length - copyLabel.Length).TrimEnd();
            }
            if (title.Length > 200 - suffix.Length) title = title.Substring(0, 200 - suffix.Length);
            var copy = new Assessment
            {
                OwnerTeacherUserId = actorUserId, CourseId = source.CourseId,
                Title = title + suffix, Description = source.Description,
                Status = AssessmentStatus.Draft, AssessmentType = source.AssessmentType,
                TargetSkill = source.TargetSkill, DurationMinutes = source.DurationMinutes,
                MaxAttempts = source.MaxAttempts, PassingScore = source.PassingScore
            };
            foreach (var item in items)
            {
                // Link có ID mới, nhưng QuestionId giữ nguyên. Question/Stimulus KHÔNG được mở khóa.
                copy.Questions.Add(new AssessmentQuestion
                {
                    QuestionId = item.QuestionId, OrderIndex = item.OrderIndex, Points = item.Points
                });
            }
            db.Assessments.Add(copy);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return WriteOutcome.Ok(copy.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Assessment clone failed for {AssessmentId}.", sourceId);
            return Failure(sourceId, "Chưa thể Clone Assessment. Hãy tải lại trang và thử lại.");
        }
    }

    private Task<bool> IsTeacherAsync(Guid id, CancellationToken ct) =>
        db.HasActiveRoleAsync(id, AppRoles.Teacher, ct);

    private static WriteOutcome Failure(Guid id, string message) => new(LearningAccessResult.Allowed, id,
        new Dictionary<string, string[]> { [string.Empty] = new[] { message } });
}
