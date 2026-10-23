using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Infrastructure.Learning.Content;

/// <summary>Persistence-aware strategy for one kind of course content. The caller owns the transaction and saves.</summary>
public interface ICourseContentHandler
{
    ContentKind Kind { get; }
    bool AffectsCompletion { get; }
    Task<bool> ParentMatchesAsync(Guid courseId, Guid parentId, CancellationToken ct);
    Task<ContentInput?> ReadAsync(Guid parentId, Guid id, CancellationToken ct);
    void Validate(ContentInput input, IDictionary<string, string[]> errors);
    Task<WriteOutcome> StageSaveAsync(Guid courseId, Guid parentId, Guid? id, ContentInput input, CancellationToken ct);
    Task<IReadOnlyList<CourseContentOrderEntry>> LoadOrderEntriesAsync(Guid parentId, CancellationToken ct);
}

public interface ICourseResourceDeletionHandler
{
    Task<WriteOutcome> StageDeleteAsync(Guid courseId, Guid lessonId, Guid id, CancellationToken ct);
}

public sealed record CourseContentOrderEntry(Guid Id, int Index, Action<int> Set);
