using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Application.Stimuli;

public static class StimulusAudioContent
{
    // Inspect format signatures rather than trusting browser filenames or Content-Type headers.
    // This accepts MP3/WAV/Ogg signatures; it does not replace decoding or malware scanning.
    public static string? DetectContentType(byte[] bytes)
    {
        if (bytes.Length < 32 || bytes.Length > StimulusAudioResource.MaxBytes) return null;
        if (bytes.AsSpan(0, 3).SequenceEqual("ID3"u8)
            || (bytes[0] == 0xff && (bytes[1] & 0xe0) == 0xe0)) return "audio/mpeg";
        if (bytes.Length >= 44 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8)) return "audio/wav";
        if (bytes.AsSpan(0, 4).SequenceEqual("OggS"u8)) return "audio/ogg";
        return null;
    }
}
