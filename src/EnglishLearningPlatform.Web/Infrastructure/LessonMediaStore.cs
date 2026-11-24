using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Web.Infrastructure;

public sealed class LessonMediaStore(IWebHostEnvironment environment, IConfiguration configuration)
{
    private readonly string storagePath = Path.GetFullPath(configuration["LessonMedia:StoragePath"] ??
        Path.Combine(environment.ContentRootPath, "App_Data", "lesson-media"));

    public const int ImageMaxBytes = 5 * 1024 * 1024;
    public const int AudioMaxBytes = StimulusAudioResource.MaxBytes;
    public const int RequestMaxBytes = 11 * 1024 * 1024;

    public string PathFor(LessonMediaResource media) =>
        Path.Combine(storagePath, $"{media.FileId:N}.{media.Format}");

    public async Task<LessonMediaResource?> SaveAsync(IFormFile file,
        LessonResourceType type, Guid resourceId, CancellationToken ct)
    {
        await using var source = file.OpenReadStream();
        var header = new byte[(int)Math.Min(44, file.Length)];
        await source.ReadExactlyAsync(header, ct);
        var format = type switch
        {
            LessonResourceType.Image when file.Length <= ImageMaxBytes => ProfileAvatarStore.ImageType(header) switch
            {
                "image/png" => "png",
                "image/jpeg" => "jpg",
                "image/webp" => "webp",
                _ => null
            },
            LessonResourceType.Audio => StimulusAudioContent.DetectContentType(header, file.Length) switch
            {
                "audio/mpeg" => "mp3",
                "audio/wav" => "wav",
                "audio/ogg" => "ogg",
                _ => null
            },
            _ => null
        };
        if (format is null) return null;

        var media = new LessonMediaResource(resourceId, Guid.NewGuid(), format);
        var path = PathFor(media);
        Directory.CreateDirectory(storagePath);
        var created = false;
        try
        {
            await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous);
            created = true;
            await destination.WriteAsync(header, ct);
            await source.CopyToAsync(destination, ct);
        }
        catch
        {
            if (created) Delete(media);
            throw;
        }
        return media;
    }

    public void Delete(LessonMediaResource media)
    {
        try { File.Delete(PathFor(media)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
