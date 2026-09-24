using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class CourseSkill
{
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public EnglishSkill Skill { get; set; }
}
