using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Application.Stimuli;

public static class StimulusAudioContent
{
    // Inspect format signatures rather than trusting browser filenames or Content-Type headers.
    // This accepts MP3/WAV/Ogg signatures; it does not replace decoding or malware scanning.
    public static string? DetectContentType(byte[] bytes) => DetectContentType(bytes, bytes.Length);

    public static string? DetectContentType(ReadOnlySpan<byte> header, long length)
    {
        if (header.Length < 32 || length < 32 || length > StimulusAudioResource.MaxBytes) return null;
        if (header[..3].SequenceEqual("ID3"u8)
            || (header[0] == 0xff && (header[1] & 0xe0) == 0xe0)) return "audio/mpeg";
        if (length >= 44 && header[..4].SequenceEqual("RIFF"u8)
            && header.Slice(8, 4).SequenceEqual("WAVE"u8)) return "audio/wav";
        if (header[..4].SequenceEqual("OggS"u8)) return "audio/ogg";
        return null;
    }
}
