using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning.Access;

public sealed class CourseLearningResourceReader(AppDbContext db) : ILearningResourceAccessReader
{
    public LearningResourceType ResourceType => LearningResourceType.Course;

    public Task<LearningResourceAccessInfo?> ReadAsync(Guid resourceId, CancellationToken ct = default) =>
        db.Courses.AsNoTracking().Where(course => course.Id == resourceId)
            .Select(course => new LearningResourceAccessInfo(course.Id, course.OwnerTeacherUserId, course.Status, true))
            .SingleOrDefaultAsync(ct);
}

public sealed class ModuleLearningResourceReader(AppDbContext db) : ILearningResourceAccessReader
{
    public LearningResourceType ResourceType => LearningResourceType.Module;

    public Task<LearningResourceAccessInfo?> ReadAsync(Guid resourceId, CancellationToken ct = default) =>
        db.Modules.AsNoTracking().Where(module => module.Id == resourceId)
            .Select(module => new LearningResourceAccessInfo(module.CourseId, module.Course.OwnerTeacherUserId,
                module.Course.Status, module.Visibility == ModuleVisibility.Visible))
            .SingleOrDefaultAsync(ct);
}

public sealed class LessonLearningResourceReader(AppDbContext db) : ILearningResourceAccessReader
{
    public LearningResourceType ResourceType => LearningResourceType.Lesson;

    public Task<LearningResourceAccessInfo?> ReadAsync(Guid resourceId, CancellationToken ct = default) =>
        db.Lessons.AsNoTracking().Where(lesson => lesson.Id == resourceId)
            .Select(lesson => new LearningResourceAccessInfo(lesson.Module.CourseId, lesson.Module.Course.OwnerTeacherUserId,
                lesson.Module.Course.Status,
                lesson.Status == LessonStatus.Published && lesson.Module.Visibility == ModuleVisibility.Visible))
            .SingleOrDefaultAsync(ct);
}

public sealed class LessonResourceLearningResourceReader(AppDbContext db) : ILearningResourceAccessReader
{
    public LearningResourceType ResourceType => LearningResourceType.Resource;

    public Task<LearningResourceAccessInfo?> ReadAsync(Guid resourceId, CancellationToken ct = default) =>
        db.LessonResources.AsNoTracking().Where(resource => resource.Id == resourceId)
            .Select(resource => new LearningResourceAccessInfo(resource.Lesson.Module.CourseId,
                resource.Lesson.Module.Course.OwnerTeacherUserId, resource.Lesson.Module.Course.Status,
                resource.Lesson.Status == LessonStatus.Published && resource.Lesson.Module.Visibility == ModuleVisibility.Visible))
            .SingleOrDefaultAsync(ct);
}
