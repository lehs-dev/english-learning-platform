using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Identity;

public sealed record UserProfile(
    Guid UserId,
    string FullName,
    string Email,
    string Role,
    AccountStatus AccountStatus,
    DateTimeOffset CreatedAtUtc);
