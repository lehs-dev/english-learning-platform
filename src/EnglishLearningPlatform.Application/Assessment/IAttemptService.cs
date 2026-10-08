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
}
