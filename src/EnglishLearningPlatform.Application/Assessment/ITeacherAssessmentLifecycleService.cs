using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Assessment;

public interface ITeacherAssessmentLifecycleService
{
    // Chỉ Unpublish/Archive; không sửa đề hoặc chấm/finalize Attempt đang chạy.
    Task<WriteOutcome> ChangeStatusAsync(
        Guid actorUserId, Guid assessmentId, AssessmentStatus targetStatus,
        DateTimeOffset? expectedUpdatedAtUtc, CancellationToken cancellationToken = default);
}
