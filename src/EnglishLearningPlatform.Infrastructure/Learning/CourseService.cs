using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Infrastructure.Learning;

/// <summary>Compatibility facade for consumers that still need the complete course contract.</summary>
public sealed class CourseService(
    ICourseCatalogService catalog, IStudentLearningCourseService students, ICourseAuthoringService authoring) : ICourseService
{
    public Task<CatalogPage> CatalogAsync(CatalogQuery q, CancellationToken ct = default) =>
        catalog.CatalogAsync(q, ct);

    public Task<CoursePage?> PublicDetailAsync(Guid id, Guid? userId, CancellationToken ct = default) =>
        catalog.PublicDetailAsync(id, userId, ct);

    public Task<LearningOutcome<IReadOnlyList<CourseSummary>>> MyCoursesAsync(Guid userId, bool teacher, CancellationToken ct = default) =>
        students.MyCoursesAsync(userId, teacher, ct);

    public Task<LearningOutcome<CoursePage>> OpenCourseAsync(Guid userId, Guid id, bool manage, CancellationToken ct = default) =>
        students.OpenCourseAsync(userId, id, manage, ct);

    public Task<LearningOutcome<LessonPage>> OpenLessonAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        students.OpenLessonAsync(userId, id, ct);

    public Task<LearningOutcome<ResourceContent>> OpenResourceAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        students.OpenResourceAsync(userId, id, ct);

    public Task<WriteOutcome> EnrollFreeAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        students.EnrollFreeAsync(userId, id, ct);

    public Task<LearningOutcome<ModulePage>> OpenModuleAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        students.OpenModuleAsync(userId, id, ct);

    public Task<WriteOutcome> SetLessonCompletedAsync(Guid userId, Guid id, bool completed, CancellationToken ct = default) =>
        students.SetLessonCompletedAsync(userId, id, completed, ct);

    public Task<LearningOutcome<Guid>> ModuleCourseAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        authoring.ModuleCourseAsync(userId, id, ct);

    public Task<LearningOutcome<IReadOnlyDictionary<Guid, IReadOnlyList<ResourceContent>>>> AuthoringResourcesAsync(Guid userId, Guid courseId, CancellationToken ct = default) =>
        authoring.AuthoringResourcesAsync(userId, courseId, ct);

    public Task<LearningOutcome<CourseInput>> CourseInputAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        authoring.CourseInputAsync(userId, id, ct);

    public Task<WriteOutcome> SaveCourseAsync(Guid userId, Guid? id, CourseInput input, CancellationToken ct = default) =>
        authoring.SaveCourseAsync(userId, id, input, ct);

    public Task<WriteOutcome> ChangeStatusAsync(Guid userId, Guid id, CourseStatus status, CancellationToken ct = default) =>
        authoring.ChangeStatusAsync(userId, id, status, ct);

    public Task<LearningOutcome<ContentInput>> ContentInputAsync(Guid userId, Guid courseId, ContentKind kind, Guid parentId, Guid? id, CancellationToken ct = default) =>
        authoring.ContentInputAsync(userId, courseId, kind, parentId, id, ct);

    public Task<WriteOutcome> SaveContentAsync(Guid userId, Guid courseId, ContentKind kind, Guid parentId, Guid? id, ContentInput input, CancellationToken ct = default) =>
        authoring.SaveContentAsync(userId, courseId, kind, parentId, id, input, ct);

    public Task<WriteOutcome> DeleteResourceAsync(Guid userId, Guid courseId, Guid lessonId, Guid id, CancellationToken ct = default) =>
        authoring.DeleteResourceAsync(userId, courseId, lessonId, id, ct);

    public Task<WriteOutcome> ReorderAsync(Guid userId, Guid courseId, ContentKind kind, Guid parentId, Guid[] ids, CancellationToken ct = default) =>
        authoring.ReorderAsync(userId, courseId, kind, parentId, ids, ct);
}
