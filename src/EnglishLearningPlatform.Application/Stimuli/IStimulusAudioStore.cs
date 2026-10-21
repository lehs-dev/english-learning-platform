namespace EnglishLearningPlatform.Application.Stimuli;

public interface IStimulusAudioStore
{
    // Store không có thao tác overwrite/delete. URL phụ thuộc vào nội dung file.
    Task<string> StoreAsync(byte[] content, CancellationToken cancellationToken = default);
    Task<StoredStimulusAudio?> ReadAsync(string hash, CancellationToken cancellationToken = default);
}

public sealed record StoredStimulusAudio(byte[] Content, string ContentType);

public interface IStimulusAudioAccessService
{
    // Đường dẫn hash không thay thế authorization của Question/Assessment/Course.
    Task<bool> CanReadAsync(Guid actorUserId, string resourceUrl, CancellationToken cancellationToken = default);
}
