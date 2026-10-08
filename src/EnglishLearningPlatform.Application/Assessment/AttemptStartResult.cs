namespace EnglishLearningPlatform.Application.Assessment;

public sealed record AttemptStartResult(
    AttemptStartStatus Status,
    Guid? AttemptId = null,
    DateTimeOffset? DeadlineUtc = null
);

