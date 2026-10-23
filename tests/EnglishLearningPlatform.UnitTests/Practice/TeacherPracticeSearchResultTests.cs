using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Practice;

public sealed class TeacherPracticeSearchResultTests
{
    [Fact]
    public void Constructor_ValidArguments_PreservesItemsOrderAndPagination()
    {
        var first = new TeacherPracticeListItem
        {
            Id = Guid.NewGuid(),
            Title = "Grammar practice",
            Status = PracticeStatus.Draft,
            QuestionCount = 3
        };
        var second = new TeacherPracticeListItem
        {
            Id = Guid.NewGuid(),
            Title = "Reading practice",
            Status = PracticeStatus.Published,
            QuestionCount = 5
        };
        var items = new List<TeacherPracticeListItem> { first, second };

        var result = new TeacherPracticeSearchResult(items, 12, 2, 10);

        Assert.Equal(12, result.TotalCount);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(new[] { first.Id, second.Id }, result.Items.Select(item => item.Id));
        Assert.Equal("Grammar practice", result.Items[0].Title);
        Assert.Equal(PracticeStatus.Draft, result.Items[0].Status);
        Assert.Equal(3, result.Items[0].QuestionCount);
        Assert.Equal("Reading practice", result.Items[1].Title);
        Assert.Equal(PracticeStatus.Published, result.Items[1].Status);
        Assert.Equal(5, result.Items[1].QuestionCount);
    }

    [Fact]
    public void Constructor_EmptyResult_HasEmptyItemsAndZeroPages()
    {
        var result = new TeacherPracticeSearchResult(
            Array.Empty<TeacherPracticeListItem>(), 0, 1, 10);

        Assert.NotNull(result.Items);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(10, result.PageSize);
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(9, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(21, 10, 3)]
    [InlineData(25, 5, 5)]
    [InlineData(int.MaxValue, 1, int.MaxValue)]
    [InlineData(int.MaxValue, 2, 1073741824)]
    [InlineData(int.MaxValue, int.MaxValue, 1)]
    [InlineData(int.MaxValue, int.MaxValue - 1, 2)]
    public void TotalPages_CalculatesRoundedUpCountWithoutOverflow(
        int totalCount, int pageSize, int expectedPages)
    {
        // Constructor không áp dụng giới hạn PageSize của nghiệp vụ tìm kiếm.
        // Các giá trị lớn ở đây chỉ kiểm tra phép tính không bị tràn int.
        var result = new TeacherPracticeSearchResult(
            Array.Empty<TeacherPracticeListItem>(), totalCount, 1, pageSize);

        Assert.Equal(expectedPages, result.TotalPages);
    }

    [Theory]
    [InlineData(-1, 1, 10, "totalCount")]
    [InlineData(int.MinValue, 1, 10, "totalCount")]
    [InlineData(0, 0, 10, "pageNumber")]
    [InlineData(0, -1, 10, "pageNumber")]
    [InlineData(0, int.MinValue, 10, "pageNumber")]
    [InlineData(0, 1, 0, "pageSize")]
    [InlineData(0, 1, -1, "pageSize")]
    [InlineData(0, 1, int.MinValue, "pageSize")]
    public void Constructor_InvalidPagination_ThrowsForCorrectParameter(
        int totalCount, int pageNumber, int pageSize, string expectedParameter)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TeacherPracticeSearchResult(
                Array.Empty<TeacherPracticeListItem>(), totalCount, pageNumber, pageSize));

        Assert.Equal(expectedParameter, exception.ParamName);
    }

    [Fact]
    public void Constructor_NullItems_ThrowsArgumentNullException()
    {
        // null! chỉ dành cho đầu vào sai cố ý trong test.
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TeacherPracticeSearchResult(null!, 0, 1, 10));

        Assert.Equal("items", exception.ParamName);
    }

    [Fact]
    public void Constructor_PageBeyondResults_PreservesTotalAndRequestedPage()
    {
        // Trang không có phần tử không đồng nghĩa cả bộ lọc có 0 kết quả.
        var result = new TeacherPracticeSearchResult(
            Array.Empty<TeacherPracticeListItem>(), 21, 9, 10);

        Assert.Empty(result.Items);
        Assert.Equal(21, result.TotalCount);
        Assert.Equal(9, result.PageNumber);
        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public void Constructor_CopiesList_OriginalStructuralChangesDoNotAffectResult()
    {
        var first = new TeacherPracticeListItem { Id = Guid.NewGuid() };
        var second = new TeacherPracticeListItem { Id = Guid.NewGuid() };
        var items = new List<TeacherPracticeListItem> { first, second };
        var result = new TeacherPracticeSearchResult(items, 2, 1, 10);

        items.Clear();
        items.Add(new TeacherPracticeListItem { Id = Guid.NewGuid() });

        Assert.Equal(new[] { first.Id, second.Id }, result.Items.Select(item => item.Id));
        Assert.NotSame(items, result.Items);
    }

    [Fact]
    public void Items_CannotBeModifiedThroughMutableListInterface()
    {
        var item = new TeacherPracticeListItem { Id = Guid.NewGuid() };
        var result = new TeacherPracticeSearchResult(
            new List<TeacherPracticeListItem> { item }, 1, 1, 10);

        // Kể cả khi ép về IList, danh sách vẫn phải chỉ đọc.
        var list = Assert.IsAssignableFrom<IList<TeacherPracticeListItem>>(result.Items);

        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() =>
            list.Add(new TeacherPracticeListItem()));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Throws<NotSupportedException>(() =>
            list[0] = new TeacherPracticeListItem());
        Assert.Equal(item.Id, Assert.Single(result.Items).Id);
    }
}
