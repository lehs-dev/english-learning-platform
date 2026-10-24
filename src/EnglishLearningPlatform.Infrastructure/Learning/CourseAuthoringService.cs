using System.ComponentModel.DataAnnotations;
using System.Data;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Learning.Content;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

public sealed class CourseAuthoringService(
    AppDbContext db, ILearningAccessService access, IActiveUserValidator activeUsers,
    CourseQueries queries, CourseProgressService courseProgress, CourseContentHandlerRegistry handlers,
    ICourseResourceDeletionHandler resources) : ICourseAuthoringService
{
    private Task<LearningAccessResult> Manage(Guid user, Guid course, CancellationToken ct) =>
        access.CheckAccessAsync(user, LearningResourceType.Course, course, LearningOperation.ManageContent, ct);

    public async Task<LearningOutcome<Guid>> ModuleCourseAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var allowed = await access.CheckAccessAsync(userId, LearningResourceType.Module, id, LearningOperation.ViewContent, ct);
        return new(allowed, allowed == LearningAccessResult.Allowed ? await db.Modules.Where(m => m.Id == id).Select(m => m.CourseId).SingleAsync(ct) : Guid.Empty);
    }

    public async Task<LearningOutcome<IReadOnlyDictionary<Guid, IReadOnlyList<ResourceContent>>>> AuthoringResourcesAsync(Guid userId, Guid courseId, CancellationToken ct = default)
    {
        var allowed = await Manage(userId, courseId, ct);
        if (allowed != LearningAccessResult.Allowed) return new(allowed);
        var rows = await db.LessonResources.AsNoTracking().Where(r => r.Lesson.Module.CourseId == courseId)
            .OrderBy(r => r.OrderIndex).ThenBy(r => r.Id).Select(r => new { r.LessonId, Resource = new ResourceContent(r.Id, r.Title, r.ResourceType, r.ContentText, r.ResourceUrl) }).ToListAsync(ct);
        return new(allowed, rows.GroupBy(r => r.LessonId).ToDictionary(g => g.Key, g => (IReadOnlyList<ResourceContent>)g.Select(r => r.Resource).ToList()));
    }

    public async Task<LearningOutcome<CourseInput>> CourseInputAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var allowed = await Manage(userId, id, ct);
        if (allowed != LearningAccessResult.Allowed) return new(allowed);
        var c = await db.Courses.AsNoTracking().Include(c => c.Skills).Include(c => c.Topics).SingleAsync(c => c.Id == id, ct);
        return new(allowed, new() { Title = c.Title, Description = c.Description, Objectives = c.Objectives, Level = c.Level,
            IsPaid = c.IsPaid, Price = c.Price, Skills = c.Skills.Select(s => s.Skill).ToArray(), Topics = string.Join(", ", c.Topics.Select(t => t.Topic)) });
    }

    private static Dictionary<string, string[]> Validate(object input)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), results, true);
        return results.GroupBy(r => r.MemberNames.FirstOrDefault() ?? "").ToDictionary(g => g.Key, g => g.Select(r => r.ErrorMessage!).ToArray());
    }

    private static WriteOutcome Invalid(Guid id, Dictionary<string, string[]> errors) => new(LearningAccessResult.Allowed, id, errors);

    public async Task<WriteOutcome> SaveCourseAsync(Guid userId, Guid? id, CourseInput input, CancellationToken ct = default)
    {
        var allowed = id.HasValue ? await Manage(userId, id.Value, ct) : await activeUsers.IsActiveTeacherAsync(userId, ct) ? LearningAccessResult.Allowed : LearningAccessResult.Forbidden;
        if (allowed != LearningAccessResult.Allowed) return WriteOutcome.Denied(allowed);
        input.Title = input.Title?.Trim() ?? "";
        var errors = Validate(input);
        if (!CourseRules.ValidPrice(input.IsPaid, input.Price)) errors[nameof(input.Price)] = ["Khóa học miễn phí phải có giá 0 đồng. Khóa học có phí phải có giá lớn hơn 0, tối đa 2 chữ số thập phân và không vượt quá 9.999.999.999.999.999,99 đồng."];
        if (input.Skills is null || input.Skills.Any(s => !Enum.IsDefined(s))) errors[nameof(input.Skills)] = ["Kỹ năng không hợp lệ."];
        var topics = (input.Topics ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (topics.Any(t => t.Length > 120)) errors[nameof(input.Topics)] = ["Mỗi chủ đề tối đa 120 ký tự."];
        if (errors.Count > 0) return Invalid(id ?? Guid.Empty, errors);
        var course = id.HasValue ? await db.Courses.Include(c => c.Skills).Include(c => c.Topics).SingleAsync(c => c.Id == id, ct) : new Course { OwnerTeacherUserId = userId };
        if (!id.HasValue) db.Courses.Add(course);
        course.Title = input.Title; course.Description = input.Description?.Trim(); course.Objectives = input.Objectives?.Trim();
        course.Level = input.Level; course.IsPaid = input.IsPaid; course.Price = input.Price; course.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var skillSet = input.Skills!.Distinct().ToHashSet();
        foreach (var old in course.Skills.Where(s => !skillSet.Contains(s.Skill)).ToList()) db.CourseSkills.Remove(old);
        foreach (var skill in skillSet.Where(s => course.Skills.All(old => old.Skill != s))) course.Skills.Add(new CourseSkill { Skill = skill });
        foreach (var old in course.Topics.Where(t => !topics.Contains(t.Topic, StringComparer.OrdinalIgnoreCase)).ToList()) db.CourseTopics.Remove(old);
        foreach (var topic in topics.Where(t => course.Topics.All(old => !old.Topic.Equals(t, StringComparison.OrdinalIgnoreCase))))
        {
            var added = new CourseTopic { Course = course, CourseId = course.Id, Topic = topic };
            course.Topics.Add(added);
            // IDs are assigned by BaseEntity; explicitly mark new topics as inserts when editing a tracked course.
            db.CourseTopics.Add(added);
        }
        await db.SaveChangesAsync(ct);
        return WriteOutcome.Ok(course.Id);
    }

    public async Task<WriteOutcome> ChangeStatusAsync(Guid userId, Guid id, CourseStatus status, CancellationToken ct = default)
    {
        var allowed = await Manage(userId, id, ct);
        if (allowed != LearningAccessResult.Allowed) return WriteOutcome.Denied(allowed);
        if (status is not (CourseStatus.Published or CourseStatus.Unpublished or CourseStatus.Archived)) return Invalid(id, new() { [""] = ["Trạng thái không hợp lệ."] });
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.CoursesForUpdate(id).SingleOrDefaultAsync(ct);
        var course = await queries.Graph().SingleAsync(c => c.Id == id, ct);
        if (status == CourseStatus.Published)
        {
            var errors = CourseRules.PublishErrors(course);
            if (errors.Count > 0) return Invalid(id, new() { [""] = errors.ToArray() });
        }
        course.Status = status; course.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await courseProgress.ReconcileCompletionAsync(id, ct);
        await tx.CommitAsync(ct); return WriteOutcome.Ok(id);
    }

    public async Task<LearningOutcome<ContentInput>> ContentInputAsync(Guid userId, Guid courseId, ContentKind kind, Guid parentId, Guid? id, CancellationToken ct = default)
    {
        var allowed = await Manage(userId, courseId, ct);
        if (allowed != LearningAccessResult.Allowed) return new(allowed);
        var handler = handlers.Find(kind);
        if (handler is null || !await handler.ParentMatchesAsync(courseId, parentId, ct)) return new(LearningAccessResult.NotFound);
        if (!id.HasValue) return new(allowed, new());
        var input = await handler.ReadAsync(parentId, id.Value, ct);
        return new(input is null ? LearningAccessResult.NotFound : allowed, input);
    }

    public async Task<WriteOutcome> SaveContentAsync(Guid userId, Guid courseId, ContentKind kind, Guid parentId, Guid? id, ContentInput input, CancellationToken ct = default)
    {
        var check = await ContentInputAsync(userId, courseId, kind, parentId, id, ct);
        if (check.Access != LearningAccessResult.Allowed) return WriteOutcome.Denied(check.Access);
        var handler = handlers.Find(kind)!;
        input.Title = input.Title?.Trim(); input.ContentText = input.ContentText?.Trim(); input.ResourceUrl = input.ResourceUrl?.Trim();
        var errors = Validate(input);
        handler.Validate(input, errors);
        if (errors.Count > 0) return Invalid(courseId, errors);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.CoursesForUpdate(courseId).SingleOrDefaultAsync(ct);
        var saved = await handler.StageSaveAsync(courseId, parentId, id, input, ct);
        if (!saved.Success) return saved;
        await db.SaveChangesAsync(ct);
        if (handler.AffectsCompletion) await courseProgress.ReconcileCompletionAsync(courseId, ct);
        await tx.CommitAsync(ct);
        return saved;
    }

    public async Task<WriteOutcome> DeleteResourceAsync(Guid userId, Guid courseId, Guid lessonId, Guid id, CancellationToken ct = default)
    {
        var check = await ContentInputAsync(userId, courseId, ContentKind.Resource, lessonId, id, ct);
        if (check.Access != LearningAccessResult.Allowed) return WriteOutcome.Denied(check.Access);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var deleted = await resources.StageDeleteAsync(courseId, lessonId, id, ct);
        if (!deleted.Success) return deleted;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return deleted;
    }

    public async Task<WriteOutcome> ReorderAsync(Guid userId, Guid courseId, ContentKind kind, Guid parentId, Guid[] ids, CancellationToken ct = default)
    {
        var allowed = await Manage(userId, courseId, ct);
        if (allowed != LearningAccessResult.Allowed) return WriteOutcome.Denied(allowed);
        var handler = handlers.Find(kind);
        if (handler is null || !await handler.ParentMatchesAsync(courseId, parentId, ct)) return WriteOutcome.Denied(LearningAccessResult.NotFound);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var entries = await handler.LoadOrderEntriesAsync(parentId, ct);
        if (!CourseContentOrderPlan.TryCreate(entries, ids, out var order, out var error))
            return Invalid(courseId, new() { [""] = [error!] });
        order.ApplyTemporary();
        await db.SaveChangesAsync(ct);
        order.ApplyFinal();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return WriteOutcome.Ok(courseId);
    }
}
