namespace EnglishLearningPlatform.Application.QuestionBank;

public static class QuestionLevels
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
        new[] { "A1", "A2", "B1", "B2", "C1", "C2" });

    public static string? Normalize(string? level)
    {
        var normalized = level?.Trim().ToUpperInvariant();
        return normalized is not null && All.Contains(normalized) ? normalized : null;
    }

    public static string Display(string? level) => Normalize(level) ?? "Chưa phân loại";
}
