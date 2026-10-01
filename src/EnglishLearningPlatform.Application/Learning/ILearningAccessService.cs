namespace EnglishLearningPlatform.Application.Learning;

public interface ILearningAccessService
{
    Task<LearningAccessResult> CheckAccessAsync(
        Guid userId,
        LearningResourceType resourceType,
        Guid resourceId,
        LearningOperation operation,
        CancellationToken cancellationToken = default);
}
