using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class Stimulus : BaseEntity
{
    public Guid OwnerTeacherUserId { get; set; }
    public StimulusType StimulusType { get; set; }
    public string? Title { get; set; }
    public string? ContentText { get; set; }
    public string? ResourceUrl { get; set; }

    public ICollection<Question> Questions { get; set; } = new List<Question>();
}
