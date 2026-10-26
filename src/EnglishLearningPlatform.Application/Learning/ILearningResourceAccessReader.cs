using EnglishLearningPlatform.Domain.Authorization;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Learning;

public interface ILearningResourceAccessReader
{
    LearningResourceType ResourceType { get; }
    Task<LearningResourceAccessInfo?> ReadAsync(Guid resourceId, CancellationToken ct = default);
}

public sealed record LearningResourceAccessInfo(
    Guid CourseId, Guid OwnerTeacherUserId, CourseStatus CourseStatus, bool IsVisible) : IOwnedResource
{
    Guid IOwnedResource.OwnerUserId => OwnerTeacherUserId;
}
