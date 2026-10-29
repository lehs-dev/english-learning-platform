using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning.Content;

public sealed class ResourceCourseContentHandler(AppDbContext db) : ICourseContentHandler, ICourseResourceDeletionHandler
{
    public ContentKind Kind => ContentKind.Resource;
    public bool AffectsCompletion => false;

    public Task<bool> ParentMatchesAsync(Guid courseId, Guid parentId, CancellationToken ct) =>
        db.Lessons.AnyAsync(l => l.Id == parentId && l.Module.CourseId == courseId, ct);

    public Task<ContentInput?> ReadAsync(Guid parentId, Guid id, CancellationToken ct) =>
        db.LessonResources.Where(r => r.Id == id && r.LessonId == parentId)
            .Select(r => new ContentInput { Title = r.Title, ResourceType = r.ResourceType, ContentText = r.ContentText, ResourceUrl = r.ResourceUrl })
            .SingleOrDefaultAsync(ct);

    public void Validate(ContentInput input, IDictionary<string, string[]> errors)
    {
        if (!CourseRules.ValidResource(input.ResourceType, input.ContentText, input.ResourceUrl))
            errors[input.ResourceType == LessonResourceType.Text ? nameof(input.ContentText) : nameof(input.ResourceUrl)] =
                ["Text cần nội dung; Link/Audio/Video cần URL http/https hợp lệ và loại resource hợp lệ."];
        if (!Enum.IsDefined(input.ResourceType)) errors[nameof(input.ResourceType)] = ["Loại resource không hợp lệ."];
    }

    public async Task<WriteOutcome> StageSaveAsync(Guid courseId, Guid parentId, Guid? id, ContentInput input, CancellationToken ct)
    {
        var resource = id.HasValue
            ? await db.LessonResources.SingleAsync(r => r.Id == id && r.LessonId == parentId, ct)
            : new LessonResource { LessonId = parentId, OrderIndex = (await db.LessonResources.Where(r => r.LessonId == parentId).MaxAsync(r => (int?)r.OrderIndex, ct) ?? -1) + 1 };
        resource.Title = input.Title;
        resource.ResourceType = input.ResourceType;
        resource.ContentText = input.ResourceType == LessonResourceType.Text ? input.ContentText : null;
        resource.ResourceUrl = input.ResourceType == LessonResourceType.Text ? null : input.ResourceUrl;
        if (!id.HasValue) db.LessonResources.Add(resource);
        return WriteOutcome.Ok(resource.Id);
    }

    public async Task<WriteOutcome> StageDeleteAsync(Guid courseId, Guid lessonId, Guid id, CancellationToken ct)
    {
        var lesson = await db.Lessons.Include(l => l.Resources).SingleAsync(l => l.Id == lessonId, ct);
        if (lesson.Status == LessonStatus.Published && (lesson.Resources.Count <= 1 ||
            lesson.Resources.Where(r => r.Id != id).Any(r => !CourseRules.ValidResource(r.ResourceType, r.ContentText, r.ResourceUrl))))
            return new(LearningAccessResult.Allowed, courseId, new Dictionary<string, string[]>
            {
                [""] = ["Không thể xóa nội dung cuối cùng hoặc để lại nội dung không hợp lệ trong bài học đã xuất bản. Hãy chuyển bài học về bản nháp hoặc ẩn trước."]
            });
        db.LessonResources.Remove(lesson.Resources.Single(r => r.Id == id));
        return WriteOutcome.Ok(courseId);
    }

    public async Task<IReadOnlyList<CourseContentOrderEntry>> LoadOrderEntriesAsync(Guid parentId, CancellationToken ct) =>
        (await db.LessonResources.Where(r => r.LessonId == parentId).ToListAsync(ct))
            .Select(r => new CourseContentOrderEntry(r.Id, r.OrderIndex, index => r.OrderIndex = index)).ToList();
}
