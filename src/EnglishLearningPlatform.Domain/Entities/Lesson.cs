using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class Lesson : BaseEntity
{
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public int OrderNo { get; set; }
    public string ContentMarkdown { get; set; } = string.Empty;
    public string? MediaUrl { get; set; }
    public int EstimatedMinutes { get; set; }
    public bool IsPublished { get; set; }

    public ICollection<LearningProgress> ProgressEntries { get; set; } = new List<LearningProgress>();
}
