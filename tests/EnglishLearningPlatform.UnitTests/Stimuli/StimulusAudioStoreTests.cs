using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Stimuli;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Stimuli;

public sealed class StimulusAudioStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ELP_AudioStoreUnit_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Store_DeduplicatesWithoutOverwrite_ChangedBytesGetNewUrl_AndTamperingFailsClosed()
    {
        var store = new FileStimulusAudioStore(_root);
        byte[] original = Audio();
        string url = await store.StoreAsync(original);
        Assert.Equal(url, await store.StoreAsync(original));
        Assert.Single(Directory.GetFiles(_root));
        Assert.True(StimulusAudioUrl.TryGetHash(url, out string hash));
        var read = await store.ReadAsync(hash);
        Assert.NotNull(read); Assert.Equal(original, read.Content); Assert.Equal("audio/mpeg", read.ContentType);
        byte[] changed = original.ToArray(); changed[^1] = 2;
        Assert.NotEqual(url, await store.StoreAsync(changed));
        Assert.Equal(original, (await store.ReadAsync(hash))!.Content);
        // Mô phỏng thay file ngoài ứng dụng: URL cũ không được trả bytes mới.
        await File.WriteAllBytesAsync(Path.Combine(_root, hash + ".audio"), changed);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync(hash));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.StoreAsync(original));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("abc")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa?x=1")]
    public async Task Read_RejectsTraversalOrUnpinnedUrls(string hash)
    {
        Assert.Null(await new FileStimulusAudioStore(_root).ReadAsync(hash));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task InvalidUpload_RejectsDisguisedFileOversizeAndConflictingSources()
    {
        var store = new FileStimulusAudioStore(_root);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.StoreAsync("<script>alert('not audio')</script>"u8.ToArray()));
        var validator = new CreateStimulusValidator();
        Assert.Empty(validator.Validate(new CreateStimulusRequest { StimulusType = StimulusType.Audio, AudioContent = Audio() }));
        Assert.NotEmpty(validator.Validate(new CreateStimulusRequest { StimulusType = StimulusType.Passage, ContentText = "Passage", AudioContent = Audio() }));
        Assert.NotEmpty(validator.Validate(new CreateStimulusRequest { StimulusType = StimulusType.Audio, AudioContent = Audio(), ResourceUrl = "https://external.test/a.mp3" }));
        Assert.NotEmpty(validator.Validate(new CreateStimulusRequest { StimulusType = StimulusType.Audio, AudioContent = new byte[StimulusAudioResource.MaxBytes + 1] }));
    }

    private static byte[] Audio() { byte[] bytes = new byte[64]; "ID3"u8.CopyTo(bytes); return bytes; }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
