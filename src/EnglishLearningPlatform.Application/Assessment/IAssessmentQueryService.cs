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
}
