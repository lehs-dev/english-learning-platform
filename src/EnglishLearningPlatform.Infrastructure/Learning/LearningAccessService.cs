using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Learning.Access;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

public sealed class LearningAccessService(
    AppDbContext dbContext,
    IActiveUserRoleReader users,
    LearningResourceAccessReaderRegistry readers) : ILearningAccessService
{
    public async Task<LearningAccessResult> CheckAccessAsync(
        Guid userId,
        LearningResourceType resourceType,
        Guid resourceId,
        LearningOperation operation,
        CancellationToken cancellationToken = default)
    {
        var role = await users.GetActiveRoleAsync(userId, cancellationToken);
        if (role is not (AppRoles.Student or AppRoles.Teacher))
            return LearningAccessResult.Forbidden;

        var reader = readers.Find(resourceType);
        var resource = reader is null ? null : await reader.ReadAsync(resourceId, cancellationToken);
        if (resource is null) return LearningAccessResult.NotFound;

        if (role == AppRoles.Teacher)
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

        // Enrolled students retain read access after withdrawal; archived content is read-only.
        if (operation == LearningOperation.RecordProgress
            && (resourceType != LearningResourceType.Lesson || resource.CourseStatus == CourseStatus.Archived))
            return LearningAccessResult.Forbidden;

        var enrolled = await dbContext.Enrollments.AsNoTracking().Valid().AnyAsync(
            enrollment => enrollment.StudentUserId == userId && enrollment.CourseId == resource.CourseId, cancellationToken);
        return enrolled ? LearningAccessResult.Allowed : LearningAccessResult.Forbidden;
    }
}
