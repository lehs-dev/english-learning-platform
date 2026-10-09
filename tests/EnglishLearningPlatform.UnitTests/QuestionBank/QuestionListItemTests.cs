using Xunit;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Enums;
namespace EnglishLearningPlatform.UnitTests.QuestionBank;

public sealed class QuestionListItemTests
{
    [Fact]
    public void QuestionListItem_WithValues_StoresQuestionSummary()
    {
        Guid guid = Guid.NewGuid();
        DateTimeOffset createdAt = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        QuestionListItem item = new QuestionListItem();
        item.Id = guid;
        item.Content = "What is the capital of France?";
        item.Level = "A1";
        item.Difficulty = "Easy";
        item.PrimarySkill = EnglishSkill.Reading;
        item.Status = QuestionStatus.Published;
        item.OptionCount = 4;
        item.CreatedAtUtc = createdAt;
        Assert.Equal(guid, item.Id);
        Assert.Equal("What is the capital of France?", item.Content);
        Assert.Equal("A1", item.Level);
        Assert.Equal("Easy", item.Difficulty);
        Assert.Equal(EnglishSkill.Reading, item.PrimarySkill);
        Assert.Equal(QuestionStatus.Published, item.Status);
        Assert.Equal(4, item.OptionCount);
        Assert.Equal(createdAt, item.CreatedAtUtc);
    }
    // Level và Difficulty là metadata tùy chọn.
    // Khi không được cung cấp, chúng phải giữ giá trị null.
    [Fact]
    public void QuestionListItem_WithoutOptionalMetadata_HasNullMetadata()
    {
        // Arrange: không gán Level và Difficulty.
        QuestionListItem item = new QuestionListItem
        {
            Id = Guid.NewGuid(),
            Content = "Choose the correct answer.",
            PrimarySkill = EnglishSkill.Grammar,
            Status = QuestionStatus.Draft,
            OptionCount = 2,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        // Assert: không tự tạo metadata mà Teacher chưa khai báo.
        Assert.Null(item.Level);
        Assert.Null(item.Difficulty);
    }
}