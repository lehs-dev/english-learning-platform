using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class Course : BaseEntity
{
    public Guid OwnerTeacherUserId { get; set; }
    public Guid? FinalAssessmentId { get; set; }
    public Assessment? FinalAssessment { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Objectives { get; set; }
    public CourseLevel Level { get; set; } = CourseLevel.Beginner;
    public CourseStatus Status { get; set; } = CourseStatus.Draft;

    public ICollection<CourseSkill> Skills { get; set; } = new List<CourseSkill>();
    public ICollection<CourseTopic> Topics { get; set; } = new List<CourseTopic>();
    public ICollection<Module> Modules { get; set; } = new List<Module>();
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public ICollection<Assessment> Assessments { get; set; } = new List<Assessment>();
}
