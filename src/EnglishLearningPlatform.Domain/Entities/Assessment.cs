using EnglishLearningPlatform.Domain.Authorization;
using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class Assessment : BaseEntity, IOwnedResource
{
    public Guid OwnerTeacherUserId { get; set; }
    Guid IOwnedResource.OwnerUserId => OwnerTeacherUserId;
    public Guid? CourseId { get; set; }
    public Course? Course { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AssessmentType AssessmentType { get; set; }
    public EnglishSkill? TargetSkill { get; set; }
    public int DurationMinutes { get; set; }
    public int MaxAttempts { get; set; }
    public decimal? PassingScore { get; set; }
    public AssessmentStatus Status { get; set; } = AssessmentStatus.Draft;

    public ICollection<AssessmentQuestion> Questions { get; set; } = new List<AssessmentQuestion>();
    public ICollection<Attempt> Attempts { get; set; } = new List<Attempt>();
}
