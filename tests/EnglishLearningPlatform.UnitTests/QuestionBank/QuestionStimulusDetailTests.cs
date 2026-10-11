using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Application.QuestionBank;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.QuestionBank;

public sealed class QuestionStimulusDetailTests
{
    // Kiểm tra DTO giữ đúng dữ liệu của một đoạn văn.
    [Fact]
    public void QuestionStimulusDetail_WithPassage_StoresValues()
    {
        // Arrange: chuẩn bị dữ liệu mong đợi.
        Guid id = Guid.NewGuid();
        string title = "Reading passage";
        string contentText = "This is a short reading passage.";

        // Act: tạo DTO và gán dữ liệu.
        QuestionStimulusDetail stimulus = new QuestionStimulusDetail
        {
            Id = id,
            StimulusType = StimulusType.Passage,
            Title = title,
            ContentText = contentText
        };

        // Assert: kiểm tra các property của DTO.
        Assert.Equal(id, stimulus.Id);
        Assert.Equal(StimulusType.Passage, stimulus.StimulusType);
        Assert.Equal(title, stimulus.Title);
        Assert.Equal(contentText, stimulus.ContentText);
        Assert.Null(stimulus.ResourceUrl);
    }

    // Kiểm tra DTO giữ đúng dữ liệu của một file audio.
    [Fact]
    public void QuestionStimulusDetail_WithAudio_StoresValues()
    {
        // Arrange.
        Guid id = Guid.NewGuid();
        string title = "Listening audio";
        string resourceUrl = "https://example.test/audio.mp3";

        // Act.
        QuestionStimulusDetail stimulus = new QuestionStimulusDetail
        {
            Id = id,
            StimulusType = StimulusType.Audio,
            Title = title,
            ResourceUrl = resourceUrl
        };

        // Assert.
        Assert.Equal(id, stimulus.Id);
        Assert.Equal(StimulusType.Audio, stimulus.StimulusType);
        Assert.Equal(title, stimulus.Title);
        Assert.Equal(resourceUrl, stimulus.ResourceUrl);
        Assert.Null(stimulus.ContentText);
    }

    // Kiểm tra các trường không bắt buộc có giá trị mặc định là null.
    [Fact]
    public void QuestionStimulusDetail_WithoutOptionalValues_HasNullValues()
    {
        // Arrange và Act: không gán các trường không bắt buộc.
        QuestionStimulusDetail stimulus = new QuestionStimulusDetail
        {
            Id = Guid.NewGuid(),
            StimulusType = StimulusType.Passage
        };

        // Assert.
        Assert.Null(stimulus.Title);
        Assert.Null(stimulus.ContentText);
        Assert.Null(stimulus.ResourceUrl);
    }
}