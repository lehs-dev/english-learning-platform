namespace EnglishLearningPlatform.IntegrationTests;

internal static class StimulusAudioTestData
{
    // WAV PCM mono 8 kHz rất ngắn, không chứa tài nguyên tải từ Internet.
    public static byte[] Wav()
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        writer.Write("RIFF"u8.ToArray()); writer.Write(68); writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000);
        writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8.ToArray()); writer.Write(32); writer.Write(new byte[32]);
        return memory.ToArray();
    }
}
