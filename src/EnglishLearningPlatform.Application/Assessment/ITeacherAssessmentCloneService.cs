using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Application.Assessment;

public interface ITeacherAssessmentCloneService
{
    // Version là bản Teacher đã thấy; owner luôn lấy từ tài khoản đăng nhập.
    Task<WriteOutcome> CloneAsync(Guid actorUserId, Guid sourceId,
        DateTimeOffset? expectedUpdatedAtUtc, CancellationToken cancellationToken = default);
}
