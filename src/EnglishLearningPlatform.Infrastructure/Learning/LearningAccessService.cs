using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

public sealed class LearningAccessService(
    AppDbContext dbContext,
    UserManager<ApplicationUser> userManager) : ILearningAccessService
{
    public async Task<LearningAccessResult> CheckAccessAsync(
        Guid userId,
        LearningResourceType resourceType,
        Guid resourceId,
        LearningOperation operation,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.AccountStatus != AccountStatus.Active)
            return LearningAccessResult.Forbidden;

        // Đọc role hiện tại ở server, không dùng role/id do client truyền vào.
        var roles = await userManager.GetRolesAsync(user);
        if (roles.Count != 1 || (roles[0] != AppRoles.Student && roles[0] != AppRoles.Teacher))
            return LearningAccessResult.Forbidden;

        var resource = await GetResourceAsync(resourceType, resourceId, cancellationToken);
        if (resource is null)
            return LearningAccessResult.NotFound;

        if (roles[0] == AppRoles.Teacher)
        {
            return resource.OwnerTeacherUserId == userId
                && operation is LearningOperation.ViewContent or LearningOperation.ManageContent
                ? LearningAccessResult.Allowed
                : LearningAccessResult.Forbidden;
        }

        if (operation != LearningOperation.ViewContent && operation != LearningOperation.RecordProgress)
            return LearningAccessResult.Forbidden;

        if (!resource.IsVisible || resource.CourseStatus is not (CourseStatus.Published or CourseStatus.Unpublished or CourseStatus.Archived))
            return LearningAccessResult.Forbidden;

        // Course Unpublished/Archived vẫn được xem bởi Student đã enroll.
        // Archived chỉ đọc; không cho ghi progress mới.
        if (operation == LearningOperation.RecordProgress
            && (resourceType != LearningResourceType.Lesson || resource.CourseStatus == CourseStatus.Archived))
            return LearningAccessResult.Forbidden;

        var enrolled = await dbContext.Enrollments.AsNoTracking().Valid().AnyAsync(
            enrollment => enrollment.StudentUserId == userId && enrollment.CourseId == resource.CourseId, cancellationToken);

        return enrolled ? LearningAccessResult.Allowed : LearningAccessResult.Forbidden;
    }

    private async Task<ResourceAccessInfo?> GetResourceAsync(
        LearningResourceType resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        // Ownership của Module/Lesson luôn đi ngược về Course, không có owner riêng.
        return resourceType switch
        {
            LearningResourceType.Course => await dbContext.Courses.AsNoTracking()
                .Where(course => course.Id == resourceId)
                .Select(course => new ResourceAccessInfo(course.Id, course.OwnerTeacherUserId, course.Status, true))
                .SingleOrDefaultAsync(cancellationToken),
            LearningResourceType.Module => await dbContext.Modules.AsNoTracking()
                .Where(module => module.Id == resourceId)
                .Select(module => new ResourceAccessInfo(module.CourseId, module.Course.OwnerTeacherUserId,
                    module.Course.Status, module.Visibility == ModuleVisibility.Visible))
                .SingleOrDefaultAsync(cancellationToken),
            LearningResourceType.Lesson => await dbContext.Lessons.AsNoTracking()
                .Where(lesson => lesson.Id == resourceId)
                .Select(lesson => new ResourceAccessInfo(lesson.Module.CourseId, lesson.Module.Course.OwnerTeacherUserId,
                    lesson.Module.Course.Status,
                    lesson.Status == LessonStatus.Published && lesson.Module.Visibility == ModuleVisibility.Visible))
                .SingleOrDefaultAsync(cancellationToken),
            LearningResourceType.Resource => await dbContext.LessonResources.AsNoTracking()
                .Where(r => r.Id == resourceId)
                .Select(r => new ResourceAccessInfo(r.Lesson.Module.CourseId, r.Lesson.Module.Course.OwnerTeacherUserId,
                    r.Lesson.Module.Course.Status, r.Lesson.Status == LessonStatus.Published && r.Lesson.Module.Visibility == ModuleVisibility.Visible))
                .SingleOrDefaultAsync(cancellationToken),
            _ => null
        };
    }

    private sealed record ResourceAccessInfo(
        Guid CourseId, Guid OwnerTeacherUserId, CourseStatus CourseStatus, bool IsVisible);
}
