using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.QuestionBank;
public class QuestionSearchResultTests
{
    [Fact]
    // Kiểm tra constructor giữ đúng dữ liệu được cung cấp.
    public void Constructor_ValidArguments_StoresResult()
    {
        // Arrange: tạo dữ liệu mẫu.
        QuestionListItem item = new QuestionListItem
        {
            Id = Guid.NewGuid(),
            Content = "What is the capital of France?",
            Level = "Beginner",
            Difficulty = "Easy",
            PrimarySkill = EnglishSkill.Reading,
            Status = QuestionStatus.Published,
            OptionCount = 4,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        List<QuestionListItem> items = new List<QuestionListItem> { item };
        // Act: tạo một QuestionSearchResult với dữ liệu mẫu.
        QuestionSearchResult result = new QuestionSearchResult(items,25,3,10);
        // Assert: kiểm tra các thuộc tính của result.
        Assert.Equal(25,result.TotalCount);
        Assert.Equal(3,result.PageNumber);
        Assert.Equal(10,result.PageSize);
        Assert.Equal(item.Id,result.Items[0].Id);
    }
    // Kiểm tra số trang khi không có câu, đủ trang và có câu dư.
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(25, 10, 3)]
    [InlineData(25, 5, 5)]
    public void TotalPages_CalculatesCorrectly(
        int totalCount,
        int pageSize,
        int expectedPages)
    {
        // Test này chỉ kiểm tra phép tính số trang.
        List<QuestionListItem> items =
            new List<QuestionListItem>();

        QuestionSearchResult result = new QuestionSearchResult(
            items,
            totalCount,
            1,
            pageSize);

        Assert.Equal(expectedPages, result.TotalPages);
    }

    // Không cho tạo kết quả với tổng số âm, trang hoặc kích thước sai.
    [Theory]
    [InlineData(-1, 1, 10)]
    [InlineData(0, 0, 10)]
    [InlineData(0, -1, 10)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 1, -1)]
    public void Constructor_InvalidPagination_ThrowsException(
        int totalCount,
        int pageNumber,
        int pageSize)
    {
        List<QuestionListItem> items =
            new List<QuestionListItem>();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            new QuestionSearchResult(
                items,
                totalCount,
                pageNumber,
                pageSize);
        });
    }

    // Cố tình truyền null để kiểm tra hàng rào của constructor.
    [Fact]
    public void Constructor_NullItems_ThrowsException()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            // null! chỉ tắt cảnh báo cho dữ liệu null cố ý trong test.
            // Constructor vẫn phải phát hiện và từ chối null.
            new QuestionSearchResult(null!, 0, 1, 10);
        });
    }

    // Sửa danh sách gốc không được làm mất câu hỏi trong kết quả đã tạo.
    [Fact]
    public void Constructor_CopiesList_OriginalChangesDoNotAffectResult()
    {
        List<QuestionListItem> items =
            new List<QuestionListItem>();

        items.Add(new QuestionListItem
        {
            Id = Guid.NewGuid(),
            Content = "Choose the correct answer.",
            PrimarySkill = EnglishSkill.Grammar,
            Status = QuestionStatus.Draft,
            OptionCount = 2,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        QuestionSearchResult result = new QuestionSearchResult(
            items,
            1,
            1,
            10);

        // Xóa phần tử khỏi danh sách ban đầu.
        items.Clear();

        // Kết quả phải giữ được danh sách đã chụp lúc tạo.
        Assert.Empty(items);
        Assert.Single(result.Items);
    }
}
