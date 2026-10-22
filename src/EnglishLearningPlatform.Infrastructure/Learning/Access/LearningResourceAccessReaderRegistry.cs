using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Infrastructure.Learning.Access;

public sealed class LearningResourceAccessReaderRegistry
{
    private readonly IReadOnlyDictionary<LearningResourceType, ILearningResourceAccessReader> readers;

    public LearningResourceAccessReaderRegistry(IEnumerable<ILearningResourceAccessReader> readers)
    {
        this.readers = readers.ToDictionary(reader => reader.ResourceType);
    }

    public ILearningResourceAccessReader? Find(LearningResourceType resourceType) => readers.GetValueOrDefault(resourceType);
}
