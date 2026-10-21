using System.Security.Cryptography;
using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Infrastructure.Stimuli;

public sealed class FileStimulusAudioStore(string rootPath) : IStimulusAudioStore
{
    private readonly string _root = Path.GetFullPath(rootPath);

    /// <summary>Ghi file mới rồi đổi tên nguyên tử; tuyệt đối không overwrite file hash đã có.</summary>
    public async Task<string> StoreAsync(byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        // Chụp bản riêng để caller không đổi mảng trong lúc đang ghi/hash.
        byte[] snapshot = content.ToArray();
        if (StimulusAudioResource.DetectContentType(snapshot) == null)
            throw new InvalidDataException("File âm thanh không hợp lệ hoặc vượt 10 MiB.");
        string hash = Convert.ToHexString(SHA256.HashData(snapshot)).ToLowerInvariant();
        Directory.CreateDirectory(_root);
        string target = Path.Combine(_root, hash + ".audio");
        string temporary = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".pending");
        try
        {
            await File.WriteAllBytesAsync(temporary, snapshot, cancellationToken);
            try { File.Move(temporary, target, overwrite: false); }
            catch (IOException) when (File.Exists(target))
            {
                // Upload lại cùng bytes dùng lại file. File đã hỏng KHÔNG bị ghi đè âm thầm.
                if (await ReadAsync(hash, cancellationToken) == null) throw;
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return StimulusAudioResource.Prefix + hash;
    }

    /// <summary>Kiểm tra hash mỗi lần đọc: file bị sửa trên đĩa không được phát dưới URL cũ.</summary>
    public async Task<StoredStimulusAudio?> ReadAsync(string hash, CancellationToken cancellationToken = default)
    {
        if (!StimulusAudioResource.TryGetHash(StimulusAudioResource.Prefix + hash, out _)) return null;
        string path = Path.Combine(_root, hash + ".audio");
        if (!File.Exists(path)) return null;
        // Đọc tối đa MaxBytes + 1 để file bị thay bằng file lớn không làm cạn RAM.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        using var memory = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (memory.Length <= StimulusAudioResource.MaxBytes)
        {
            int remaining = StimulusAudioResource.MaxBytes + 1 - (int)memory.Length;
            int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0) break;
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        byte[] content = memory.ToArray();
        string? contentType = StimulusAudioResource.DetectContentType(content);
        if (contentType == null || Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant() != hash)
            throw new InvalidDataException("Audio đã bị thay đổi hoặc hỏng; từ chối phát dưới URL cũ.");
        return new StoredStimulusAudio(content, contentType);
    }
}
