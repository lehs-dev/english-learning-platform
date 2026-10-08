using System.ComponentModel.DataAnnotations;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Learning;

public sealed class CatalogQuery
{
    public string? Search { get; set; }
    public CourseLevel? Level { get; set; }
    public EnglishSkill? Skill { get; set; }
    public string? Topic { get; set; }
    public Guid? Teacher { get; set; }
    public bool? Paid { get; set; }
    public int Page { get; set; } = 1;
}
public sealed record TeacherOption(Guid Id, string Name);
public sealed record CatalogPage(CatalogQuery Query, IReadOnlyList<CourseSummary> Courses, int Total,
    IReadOnlyList<TeacherOption> Teachers, IReadOnlyList<string> Topics)
{
    public const int PageSize = 9;
    public int Pages => Math.Max(1, (Total + PageSize - 1) / PageSize);
}
public sealed record CourseSummary(Guid Id, Guid OwnerId, string Title, string? Description, string? Objectives,
    string Teacher, CourseLevel Level, CourseStatus Status, bool IsPaid, decimal Price,
    IReadOnlyList<EnglishSkill> Skills, IReadOnlyList<string> Topics, int ModuleCount, int LessonCount)
{
    public CourseProgress? Progress { get; init; }
}
public sealed record CourseProgress(int CompletedLessons, int TotalLessons, DateTimeOffset? CompletedAtUtc, Guid? ContinueLessonId)
{
    public decimal? Percentage => TotalLessons == 0 ? null : Math.Round(CompletedLessons * 100m / TotalLessons, 2);
}
// Public structure deliberately has no resource fields.
public sealed record LessonOutline(Guid Id, string Title, LessonStatus Status)
{
    public bool IsCompleted { get; init; }
}
public sealed record ModuleOutline(Guid Id, string Title, string? Description, ModuleVisibility Visibility,
    IReadOnlyList<LessonOutline> Lessons);
public sealed record CoursePage(CourseSummary Course, IReadOnlyList<ModuleOutline> Modules,
    bool CanLearn, bool IsOwner, int EnrollmentCount, IReadOnlyList<string> PublishErrors)
{
    public CourseProgress? Progress { get; init; }
}
public sealed record ModulePage(Guid Id, Guid CourseId, string CourseTitle, string Title, string? Description,
    CourseStatus CourseStatus, bool IsOwner, IReadOnlyList<LessonOutline> Lessons, CourseProgress? Progress);
public sealed record ResourceContent(Guid Id, string? Title, LessonResourceType Type, string? Text, string? Url);
public sealed record LessonPage(Guid Id, Guid CourseId, string CourseTitle, string Title,
    IReadOnlyList<ResourceContent> Resources)
{
    public Guid ModuleId { get; init; }
    public string ModuleTitle { get; init; } = "";
    public bool IsCompleted { get; init; }
    public bool CanRecordProgress { get; init; }
    public CourseStatus CourseStatus { get; init; }
    public CourseProgress? Progress { get; init; }
    public LessonOutline? Previous { get; init; }
    public LessonOutline? Next { get; init; }
}
public sealed record LearningOutcome<T>(LearningAccessResult Access, T? Value = default);
public sealed record WriteOutcome(LearningAccessResult Access, Guid Id, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Success => Access == LearningAccessResult.Allowed && Errors.Count == 0;
    public static WriteOutcome Ok(Guid id) => new(LearningAccessResult.Allowed, id, new Dictionary<string, string[]>());
    public static WriteOutcome Denied(LearningAccessResult access) => new(access, Guid.Empty, new Dictionary<string, string[]>());
}
public sealed class CourseInput
{
    [Required, StringLength(200)] public string Title { get; set; } = "";
    [StringLength(4000)] public string? Description { get; set; }
    [StringLength(4000)] public string? Objectives { get; set; }
    [EnumDataType(typeof(CourseLevel))] public CourseLevel Level { get; set; } = CourseLevel.Beginner;
    public EnglishSkill[] Skills { get; set; } = [];
    [StringLength(4000)] public string? Topics { get; set; }
    public bool IsPaid { get; set; }
    public decimal Price { get; set; }
}
public enum ContentKind { Module, Lesson, Resource }
public sealed class ContentInput
{
    [StringLength(200)] public string? Title { get; set; }
    [StringLength(4000)] public string? Description { get; set; }
    public ModuleVisibility Visibility { get; set; } = ModuleVisibility.Visible;
    public LessonStatus Status { get; set; } = LessonStatus.Draft;
    public LessonResourceType ResourceType { get; set; } = LessonResourceType.Text;
    public string? ContentText { get; set; }
    [StringLength(2048)] public string? ResourceUrl { get; set; }
}
public interface ICourseService
{
    Task<LearningOutcome<IReadOnlyDictionary<Guid, IReadOnlyList<ResourceContent>>>> AuthoringResourcesAsync(Guid userId, Guid courseId, CancellationToken ct = default);
    Task<CatalogPage> CatalogAsync(CatalogQuery query, CancellationToken ct = default);
    Task<CoursePage?> PublicDetailAsync(Guid id, Guid? userId, CancellationToken ct = default);
    Task<LearningOutcome<IReadOnlyList<CourseSummary>>> MyCoursesAsync(Guid userId, bool teacher, CancellationToken ct = default);
    Task<LearningOutcome<CoursePage>> OpenCourseAsync(Guid userId, Guid id, bool manage, CancellationToken ct = default);
    Task<LearningOutcome<LessonPage>> OpenLessonAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<LearningOutcome<ModulePage>> OpenModuleAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<WriteOutcome> SetLessonCompletedAsync(Guid userId, Guid id, bool completed, CancellationToken ct = default);
    Task<LearningOutcome<ResourceContent>> OpenResourceAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<LearningOutcome<Guid>> ModuleCourseAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<LearningOutcome<CourseInput>> CourseInputAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<WriteOutcome> SaveCourseAsync(Guid userId, Guid? id, CourseInput input, CancellationToken ct = default);
    Task<LearningOutcome<ContentInput>> ContentInputAsync(Guid userId, Guid courseId, ContentKind kind, Guid parentId, Guid? id, CancellationToken ct = default);
    Task<WriteOutcome> SaveContentAsync(Guid userId, Guid courseId, ContentKind kind, Guid parentId, Guid? id, ContentInput input, CancellationToken ct = default);
    Task<WriteOutcome> DeleteResourceAsync(Guid userId, Guid courseId, Guid lessonId, Guid id, CancellationToken ct = default);
    Task<WriteOutcome> ReorderAsync(Guid userId, Guid courseId, ContentKind kind, Guid parentId, Guid[] ids, CancellationToken ct = default);
    Task<WriteOutcome> ChangeStatusAsync(Guid userId, Guid id, CourseStatus status, CancellationToken ct = default);
    Task<WriteOutcome> EnrollFreeAsync(Guid userId, Guid id, CancellationToken ct = default);
}
