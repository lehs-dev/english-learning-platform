using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Practice;

public sealed class TeacherPracticeSearchRequestTests
{
    [Fact]
    public void NewRequest_HasNoFiltersAndUsesFirstPageOfTenItems()
    {
        var request = new TeacherPracticeSearchRequest();

        Assert.Null(request.Search);
        Assert.Null(request.Status);
        Assert.Null(request.CourseId);
        Assert.Equal(1, request.PageNumber);
        Assert.Equal(10, request.PageSize);
    }

    [Fact]
    public void Request_PreservesConfiguredFiltersAndPagination()
    {
        var courseId = Guid.NewGuid();
        var request = new TeacherPracticeSearchRequest
        {
            Search = "Grammar",
            Status = PracticeStatus.Draft,
            CourseId = courseId,
            PageNumber = 3,
            PageSize = 20
        };

        Assert.Equal("Grammar", request.Search);
        Assert.Equal(PracticeStatus.Draft, request.Status);
        Assert.Equal(courseId, request.CourseId);
        Assert.Equal(3, request.PageNumber);
        Assert.Equal(20, request.PageSize);
    }

    [Theory]
    [InlineData(PracticeStatus.Draft)]
    [InlineData(PracticeStatus.Published)]
    [InlineData(PracticeStatus.Unpublished)]
    public void Request_CanRepresentEachPracticeStatusFilter(PracticeStatus status)
    {
        var request = new TeacherPracticeSearchRequest { Status = status };

        Assert.Equal(status, request.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("  Grammar  ")]
    public void Request_PreservesSearchValueForServiceToProcess(string? search)
    {
        // DTO chỉ giữ dữ liệu. Chuẩn hóa từ khóa thuộc trách nhiệm của service.
        var request = new TeacherPracticeSearchRequest { Search = search };

        Assert.Equal(search, request.Search);
    }

    [Fact]
    public void ChangingOneRequest_DoesNotChangeAnotherRequest()
    {
        var first = new TeacherPracticeSearchRequest();
        var second = new TeacherPracticeSearchRequest();

        first.Search = "Vocabulary";
        first.Status = PracticeStatus.Published;
        first.CourseId = Guid.NewGuid();
        first.PageNumber = 5;
        first.PageSize = 30;

        Assert.Null(second.Search);
        Assert.Null(second.Status);
        Assert.Null(second.CourseId);
        Assert.Equal(1, second.PageNumber);
        Assert.Equal(10, second.PageSize);
    }
}
