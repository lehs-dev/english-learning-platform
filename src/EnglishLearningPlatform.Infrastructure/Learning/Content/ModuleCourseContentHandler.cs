using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning.Content;

public sealed class ModuleCourseContentHandler(AppDbContext db) : ICourseContentHandler
{
    public ContentKind Kind => ContentKind.Module;
    public bool AffectsCompletion => true;

    public Task<bool> ParentMatchesAsync(Guid courseId, Guid parentId, CancellationToken ct) =>
        Task.FromResult(parentId == courseId);

    public Task<ContentInput?> ReadAsync(Guid parentId, Guid id, CancellationToken ct) =>
        db.Modules.Where(m => m.Id == id && m.CourseId == parentId)
            .Select(m => new ContentInput { Title = m.Title, Description = m.Description, Visibility = m.Visibility })
            .SingleOrDefaultAsync(ct);

    public void Validate(ContentInput input, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(input.Title)) errors[nameof(input.Title)] = ["Tên không được để trống."];
        if (!Enum.IsDefined(input.Visibility)) errors[nameof(input.Visibility)] = ["Trạng thái hiển thị chương học không hợp lệ."];
    }

    public async Task<WriteOutcome> StageSaveAsync(Guid courseId, Guid parentId, Guid? id, ContentInput input, CancellationToken ct)
    {
        var module = id.HasValue
            ? await db.Modules.SingleAsync(m => m.Id == id && m.CourseId == parentId, ct)
            : new Module { CourseId = parentId, OrderIndex = (await db.Modules.Where(m => m.CourseId == parentId).MaxAsync(m => (int?)m.OrderIndex, ct) ?? -1) + 1 };
        module.Title = input.Title!;
        module.Description = input.Description?.Trim();
        module.Visibility = input.Visibility;
        if (!id.HasValue) db.Modules.Add(module);
        return WriteOutcome.Ok(module.Id);
    }

    public async Task<IReadOnlyList<CourseContentOrderEntry>> LoadOrderEntriesAsync(Guid parentId, CancellationToken ct) =>
        (await db.Modules.Where(m => m.CourseId == parentId).ToListAsync(ct))
            .Select(m => new CourseContentOrderEntry(m.Id, m.OrderIndex, index => m.OrderIndex = index)).ToList();
}
