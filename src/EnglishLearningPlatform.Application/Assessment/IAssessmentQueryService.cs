namespace EnglishLearningPlatform.Application.Assessment;

public interface IAssessmentQueryService
{
    Task<AssessmentOverviewModel?> GetAssessmentOverviewAsync(
        Guid studentId,
        Guid assessmentId,
        CancellationToken ct = default);

    Task<AssessmentTakingSheet?> GetTakingSheetAsync(
        Guid studentId,
        Guid attemptId,
        CancellationToken ct = default);

    Task<AssessmentResultView?> GetAttemptResultAsync(
        Guid studentId,
        Guid attemptId,
        CancellationToken ct = default);

    // Xem tất cả lịch sử hoặc lọc một Assessment, kể cả đề đã ngừng nhận lượt mới.
    Task<IReadOnlyList<AssessmentHistoryView>?> GetAttemptHistoryAsync(
        Guid studentId,
        Guid? assessmentId = null,
        CancellationToken ct = default);

    // Chỉ tìm ID để mở trang chấm lượt quá hạn, không trả câu hỏi hoặc tự ghi dữ liệu.
    Task<Guid?> FindExpiredAttemptIdAsync(
        Guid studentId,
        Guid assessmentId,
        Guid? attemptId,
        CancellationToken ct = default);
}
