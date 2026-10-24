using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.QuestionBank;

public sealed class QuestionDetailTests
{
    // Kiểm tra giá trị mặc định, đặc biệt là Content và Options không null.
    [Fact]
    public void QuestionDetail_NewInstance_HasExpectedDefaults()
    {
        // Arrange và Act.
        QuestionDetail detail = new QuestionDetail();

        // Assert.
        Assert.Equal(string.Empty, detail.Content);
        Assert.NotNull(detail.Options);
        Assert.Empty(detail.Options);
        Assert.Equal(QuestionStatus.Draft, detail.Status);

        Assert.Null(detail.Level);
        Assert.Null(detail.Explanation);
        Assert.Null(detail.Stimulus);
    }

    // Kiểm tra DTO giữ đúng các thông tin được gán cho câu hỏi.
    [Fact]
    public void QuestionDetail_WithValues_StoresQuestionInformation()
    {
        // Arrange: dùng thời gian cố định để kết quả test ổn định.
        Guid id = Guid.NewGuid();

        DateTimeOffset createdAt = new DateTimeOffset(
            2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

        DateTimeOffset updatedAt = createdAt.AddHours(1);

        // Act.
        QuestionDetail detail = new QuestionDetail
        {
            Id = id,
            Content = "Choose the correct answer.",
            PrimarySkill = EnglishSkill.Reading,
            Level = "A1",
            Explanation = "Option 2 is correct.",
            Status = QuestionStatus.Published,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = updatedAt
        };

        // Assert.
        Assert.Equal(id, detail.Id);
        Assert.Equal("Choose the correct answer.", detail.Content);
        Assert.Equal(EnglishSkill.Reading, detail.PrimarySkill);
        Assert.Equal("A1", detail.Level);
        Assert.Equal("Option 2 is correct.", detail.Explanation);
        Assert.Equal(QuestionStatus.Published, detail.Status);
        Assert.Equal(createdAt, detail.CreatedAtUtc);
        Assert.Equal(updatedAt, detail.UpdatedAtUtc);
    }

    // Kiểm tra DTO chứa được cả danh sách option và passage/audio.
    [Fact]
    public void QuestionDetail_WithOptionsAndStimulus_StoresRelatedData()
    {
        // Arrange: tạo danh sách hai option.
        List<QuestionOptionItem> options = new List<QuestionOptionItem>();

        options.Add(new QuestionOptionItem
        {
            Content = "Option 1",
            OrderIndex = 0,
            IsCorrect = false
        });

        options.Add(new QuestionOptionItem
        {
            Content = "Option 2",
            OrderIndex = 1,
            IsCorrect = true
        });

        QuestionStimulusDetail stimulus = new QuestionStimulusDetail
        {
            Id = Guid.NewGuid(),
            StimulusType = StimulusType.Passage,
            ContentText = "A short reading passage."
        };

        // Act.
        QuestionDetail detail = new QuestionDetail
        {
            Content = "Choose the correct answer.",
            PrimarySkill = EnglishSkill.Reading,
            Options = options,
            Stimulus = stimulus
        };

        // Assert.
        Assert.Equal(2, detail.Options.Count);
        Assert.Equal("Option 1", detail.Options[0].Content);
        Assert.Equal("Option 2", detail.Options[1].Content);
        Assert.False(detail.Options[0].IsCorrect);
        Assert.True(detail.Options[1].IsCorrect);

        // Kiểm tra Stimulus là đúng đối tượng đã được gán vào DTO.
        Assert.Same(stimulus, detail.Stimulus);
    }
}