using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Application.Assessment;

public interface ITeacherAssessmentPublishService
{
    // Chỉ Publish dữ liệu đã lưu trong database.
    // Không nhận cấu hình, owner hoặc danh sách câu hỏi từ form.
    Task<WriteOutcome> PublishAsync(
        Guid actorUserId,
        Guid assessmentId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);
}