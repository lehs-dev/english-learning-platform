using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Domain.Common;

public readonly record struct LessonMediaResource(Guid ResourceId, Guid FileId, string Format)
{
    public const string RoutePrefix = "/LessonMedia/";

    public string Url => $"{RoutePrefix}{ResourceId:N}/{FileId:N}/{Format}";

    public string? ContentType => Format switch
    {
        "png" => "image/png",
        "jpg" => "image/jpeg",
        "webp" => "image/webp",
        "mp3" => "audio/mpeg",
        "wav" => "audio/wav",
        "ogg" => "audio/ogg",
        _ => null
    };

    public bool Matches(LessonResourceType type) => type switch
    {
        LessonResourceType.Image => Format is "png" or "jpg" or "webp",
        LessonResourceType.Audio => Format is "mp3" or "wav" or "ogg",
        _ => false
    };

    public static bool TryParse(string? url, out LessonMediaResource media)
    {
        media = default;
        if (url is null || !url.StartsWith(RoutePrefix, StringComparison.Ordinal)) return false;

        var segments = url[RoutePrefix.Length..].Split('/');
        if (segments.Length != 3 || !Guid.TryParseExact(segments[0], "N", out var resourceId) ||
            !Guid.TryParseExact(segments[1], "N", out var fileId) || resourceId == Guid.Empty || fileId == Guid.Empty)
            return false;

        media = new(resourceId, fileId, segments[2]);
        return media.ContentType is not null && media.Url == url;
    }
}
