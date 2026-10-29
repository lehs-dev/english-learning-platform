namespace EnglishLearningPlatform.Web.Infrastructure;

// Avatars are private files, served only through the authenticated profile action.
public static class ProfileAvatarStore
{
    public const int MaxBytes = 2 * 1024 * 1024;

    public static string PathFor(IWebHostEnvironment environment, Guid userId) =>
        Path.Combine(environment.ContentRootPath, "App_Data", "avatars", userId.ToString("N"));

    public static string? ImageType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return "image/jpeg";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }
}
