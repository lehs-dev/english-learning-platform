using System.Diagnostics.CodeAnalysis;

namespace EnglishLearningPlatform.Infrastructure.Learning.Content;

/// <summary>Plans both reorder passes before any tracked entity is changed.</summary>
public sealed class CourseContentOrderPlan
{
    private readonly IReadOnlyList<CourseContentOrderEntry> entries;
    private readonly Guid[] ids;
    private readonly int temporaryStart;

    private CourseContentOrderPlan(IReadOnlyList<CourseContentOrderEntry> entries, Guid[] ids, int temporaryStart)
    {
        this.entries = entries;
        this.ids = ids;
        this.temporaryStart = temporaryStart;
    }

    public static bool TryCreate(IReadOnlyList<CourseContentOrderEntry> entries, Guid[]? ids,
        [NotNullWhen(true)] out CourseContentOrderPlan? plan, out string? error)
    {
        plan = null;
        error = null;
        if (ids is null || ids.Length != entries.Count || ids.Distinct().Count() != ids.Length || entries.Any(e => !ids.Contains(e.Id)))
        {
            error = "Thứ tự phải chứa đúng mỗi ID của cùng parent một lần.";
            return false;
        }
        // Temporary values stay below every existing and final index in either save.
        var start = Math.Min(-1L, entries.Count == 0 ? -1L : (long)entries.Min(e => e.Index) - entries.Count - 1);
        if (start < int.MinValue)
        {
            error = "OrderIndex vượt giới hạn; cần chuẩn hóa dữ liệu.";
            return false;
        }
        plan = new(entries, ids, (int)start);
        return true;
    }

    public void ApplyTemporary()
    {
        for (var i = 0; i < entries.Count; i++) entries[i].Set(temporaryStart + i);
    }

    public void ApplyFinal()
    {
        for (var i = 0; i < ids.Length; i++) entries.Single(e => e.Id == ids[i]).Set(i);
    }
}
