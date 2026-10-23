using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning.Content;

public sealed class LessonCourseContentHandler(AppDbContext db) : ICourseContentHandler
{
    public ContentKind Kind => ContentKind.Lesson;
    public bool AffectsCompletion => true;

    public Task<bool> ParentMatchesAsync(Guid courseId, Guid parentId, CancellationToken ct) =>
        db.Modules.AnyAsync(m => m.Id == parentId && m.CourseId == courseId, ct);

    public Task<ContentInput?> ReadAsync(Guid parentId, Guid id, CancellationToken ct) =>
        db.Lessons.Where(l => l.Id == id && l.ModuleId == parentId)
            .Select(l => new ContentInput { Title = l.Title, Status = l.Status }).SingleOrDefaultAsync(ct);

    public void Validate(ContentInput input, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(input.Title)) errors[nameof(input.Title)] = ["Tên không được để trống."];
        if (!Enum.IsDefined(input.Status)) errors[nameof(input.Status)] = ["Status không hợp lệ."];
    }

    public async Task<WriteOutcome> StageSaveAsync(Guid courseId, Guid parentId, Guid? id, ContentInput input, CancellationToken ct)
    {
        var lesson = id.HasValue
            ? await db.Lessons.Include(l => l.Resources).SingleAsync(l => l.Id == id && l.ModuleId == parentId, ct)
            : new Lesson { ModuleId = parentId, OrderIndex = (await db.Lessons.Where(l => l.ModuleId == parentId).MaxAsync(l => (int?)l.OrderIndex, ct) ?? -1) + 1 };
        if (input.Status == LessonStatus.Published && (lesson.Resources.Count == 0 ||
            lesson.Resources.Any(r => !CourseRules.ValidResource(r.ResourceType, r.ContentText, r.ResourceUrl))))
            return new(LearningAccessResult.Allowed, courseId, new Dictionary<string, string[]>
            {
                [nameof(input.Status)] = ["Thêm resource hợp lệ trước khi chuyển Lesson sang Published."]
            });
        lesson.Title = input.Title!;
        lesson.Status = input.Status;
        if (!id.HasValue) db.Lessons.Add(lesson);
        return WriteOutcome.Ok(lesson.Id);
    }

    public async Task<IReadOnlyList<CourseContentOrderEntry>> LoadOrderEntriesAsync(Guid parentId, CancellationToken ct) =>
        (await db.Lessons.Where(l => l.ModuleId == parentId).ToListAsync(ct))
            .Select(l => new CourseContentOrderEntry(l.Id, l.OrderIndex, index => l.OrderIndex = index)).ToList();
}
