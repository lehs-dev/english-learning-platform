using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Infrastructure.Stimuli;

internal static class StimulusAudioIntegrity
{
    // Một Stimulus dùng cho nhiều câu chỉ đọc/hash file một lần trong request.
    public static async Task<string?> GetErrorAsync(IEnumerable<Stimulus?> stimuli, IStimulusAudioStore store, CancellationToken ct)
    {
        foreach (string? url in stimuli.Where(s => s?.StimulusType == StimulusType.Audio).Select(s => s!.ResourceUrl).Distinct())
        {
            // Dữ liệu lịch sử dùng URL ngoài vẫn được giữ, không tự tải hoặc sửa nó.
            if (!StimulusAudioResource.TryGetHash(url, out string hash)) continue;
            try
            {
                if (await store.ReadAsync(hash, ct) == null) return "Không tìm thấy audio đã lưu; hãy khôi phục kho audio trước khi công bố/sửa đề.";
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException)
            { return "Audio đã lưu bị hỏng hoặc không đọc được. Không thể công bố/sửa đề."; }
        }
        return null;
    }
}
