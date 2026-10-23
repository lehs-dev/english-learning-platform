using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Common;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Stimuli;

public sealed class StimulusAudioResourceTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void PlaybackUrl_PreservesExistingRouteAndLogicalIdentifier()
    {
        Assert.Equal("/StimulusAudio/Play/", StimulusAudioConstants.RoutePrefix);
        Assert.True(StimulusAudioResource.IsValidHash(Hash));
        Assert.True(StimulusAudioUrl.TryGetHash("/StimulusAudio/Play/" + Hash, out var parsed));
        Assert.Equal(Hash, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(Hash)]
    [InlineData("https://example.test/StimulusAudio/Play/" + Hash)]
    [InlineData("/stimulusaudio/play/" + Hash)]
    [InlineData("/StimulusAudio/Play/" + Hash + "?download=1")]
    [InlineData("/StimulusAudio/Play/" + Hash + "#fragment")]
    [InlineData("/StimulusAudio/Play/../" + Hash)]
    [InlineData("/StimulusAudio/Play/" + Hash + "/")]
    [InlineData("/StimulusAudio/Play/0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
    public void PlaybackUrl_RejectsNoncanonicalPaths(string? url)
    {
        Assert.False(StimulusAudioUrl.TryGetHash(url, out var hash));
        Assert.Empty(hash);
    }

    [Theory]
    [InlineData("ID3", 32, "audio/mpeg")]
    [InlineData("OggS", 32, "audio/ogg")]
    [InlineData("RIFF", 44, "audio/wav")]
    [InlineData("ID3", 31, null)]
    [InlineData("OggS", 31, null)]
    [InlineData("RIFF", 43, null)]
    [InlineData("Text", 64, null)]
    public void AudioContent_PreservesFormatAndMinimumSizeValidation(string signature, int length, string? expected)
    {
        var bytes = new byte[length];
        System.Text.Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        if (signature == "RIFF") "WAVE"u8.CopyTo(bytes.AsSpan(8));

        Assert.Equal(expected, StimulusAudioContent.DetectContentType(bytes));
    }

    [Fact]
    public void AudioContent_PreservesMp3FrameAndMaximumSizeValidation()
    {
        var bytes = new byte[StimulusAudioResource.MaxBytes];
        bytes[0] = 0xff;
        bytes[1] = 0xe0;
        Assert.Equal("audio/mpeg", StimulusAudioContent.DetectContentType(bytes));

        var oversized = new byte[StimulusAudioResource.MaxBytes + 1];
        "ID3"u8.CopyTo(oversized);
        Assert.Null(StimulusAudioContent.DetectContentType(oversized));
    }
}
