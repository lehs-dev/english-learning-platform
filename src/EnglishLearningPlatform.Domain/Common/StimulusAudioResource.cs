namespace EnglishLearningPlatform.Domain.Common;

public static class StimulusAudioResource
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const string Prefix = "/StimulusAudio/Play/";

    // Chỉ nhận đúng đường dẫn do server sinh, không nhận ../, query hoặc URL máy khác.
    public static bool TryGetHash(string? url, out string hash)
    {
        hash = string.Empty;
        if (url == null || !url.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        string candidate = url[Prefix.Length..];
        if (candidate.Length != 64 || candidate.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))) return false;
        hash = candidate;
        return true;
    }

    // Không tin đuôi file hoặc Content-Type từ browser. MVP nhận MP3/WAV/Ogg.
    // Đây là kiểm tra chữ ký định dạng, không thay thế bộ giải mã hoặc quét mã độc.
    public static string? DetectContentType(byte[] bytes)
    {
        if (bytes.Length < 32 || bytes.Length > MaxBytes) return null;
        if (bytes.AsSpan(0, 3).SequenceEqual("ID3"u8)
            || (bytes[0] == 0xff && (bytes[1] & 0xe0) == 0xe0)) return "audio/mpeg";
        if (bytes.Length >= 44 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8)) return "audio/wav";
        if (bytes.AsSpan(0, 4).SequenceEqual("OggS"u8)) return "audio/ogg";
        return null;
    }
}
