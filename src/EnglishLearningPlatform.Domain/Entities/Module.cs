using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class Module : BaseEntity
{
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int OrderIndex { get; set; }
    public ModuleVisibility Visibility { get; set; } = ModuleVisibility.Visible;

    public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
    public ICollection<Practice> Practices { get; set; } = new List<Practice>();
}
