using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Infrastructure.Learning.Content;

public sealed class CourseContentHandlerRegistry
{
    private readonly IReadOnlyDictionary<ContentKind, ICourseContentHandler> handlers;

    public CourseContentHandlerRegistry(IEnumerable<ICourseContentHandler> handlers)
    {
        this.handlers = handlers.ToDictionary(handler => handler.Kind);
    }

    public ICourseContentHandler? Find(ContentKind kind) => handlers.GetValueOrDefault(kind);
}
