using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Assessment;


public sealed record AttemptFinalizeResult(
    AttemptFinalizeStatus Status,
    Guid? AttemptId = null,
    decimal? OverallScore = null,
    bool? Passed = null,
    AttemptFinalizationReason? Reason = null);