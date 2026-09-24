using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class LessonResource : BaseEntity
{
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;
    public LessonResourceType ResourceType { get; set; }
    public string? Title { get; set; }
    public string? ContentText { get; set; }
    public string? ResourceUrl { get; set; }
    public int OrderIndex { get; set; }
}
