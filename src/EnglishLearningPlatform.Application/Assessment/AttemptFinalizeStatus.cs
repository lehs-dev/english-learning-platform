using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Assessment;

public enum AttemptFinalizeStatus
{
    Graded,
    AlreadyGraded,
    Forbidden,
    NotFound,
    Unavailable
}