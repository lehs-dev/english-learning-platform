using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class Lesson : BaseEntity
{
    public Guid ModuleId { get; set; }
    public Module Module { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public LessonStatus Status { get; set; } = LessonStatus.Draft;

    public ICollection<LessonResource> Resources { get; set; } = new List<LessonResource>();
    public ICollection<LessonProgress> ProgressEntries { get; set; } = new List<LessonProgress>();
    public ICollection<Practice> Practices { get; set; } = new List<Practice>();
}
