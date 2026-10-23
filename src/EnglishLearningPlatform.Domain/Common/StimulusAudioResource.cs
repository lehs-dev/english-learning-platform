namespace EnglishLearningPlatform.Domain.Common;

/// <summary>Storage-independent limits and identifiers for immutable stimulus audio.</summary>
public static class StimulusAudioResource
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const int HashLength = 64;

    public static bool IsValidHash(string? hash) =>
        hash is { Length: HashLength }
        && hash.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
}
