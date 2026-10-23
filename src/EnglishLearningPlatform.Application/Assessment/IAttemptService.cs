namespace EnglishLearningPlatform.Application.Assessment;

public interface IAttemptService
{
    Task<AttemptStartResult> StartAsync(
        Guid studentId,
        Guid assessmentId,
        CancellationToken ct = default
    );

    Task<AttemptFinalizeResult> FinalizeAsync(
    Guid studentId,
    Guid attemptId,
    CancellationToken ct = default);

    // Chỉ chấm lượt đã hết giờ; đọc trang khi còn hạn không được nộp bài sớm.
    Task<AttemptFinalizeResult> FinalizeExpiredAsync(
        Guid studentId,
        Guid attemptId,
        CancellationToken ct = default);

    Task<AttemptSaveStatus> SaveAnswerAsync(
    Guid studentId,
    Guid attemptId,
    Guid assessmentQuestionId,
    Guid? selectedOptionId,
    CancellationToken ct = default);
}
