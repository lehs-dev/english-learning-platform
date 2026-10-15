using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Web.Models.Practice;
using Microsoft.AspNetCore.Mvc.Rendering;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Project IntegrationTests đã tham chiếu Web nên kiểm tra được ViewModel.
// Đây là test model thuần, không dùng fixture, Docker hay database.
public sealed class TeacherPracticeIndexViewModelTests
{
    [Fact]
    public void Constructor_PreservesQueryAndResultForDisplayingFiltersAndPagination()
    {
        var courseId = Guid.NewGuid();
        var item = new TeacherPracticeListItem
        {
            Id = Guid.NewGuid(), Title = "Grammar practice",
            Status = PracticeStatus.Draft, QuestionCount = 3,
            CourseId = courseId, CourseTitle = "English course"
        };
        var query = new TeacherPracticeSearchRequest
        {
            Search = "Grammar", Status = PracticeStatus.Draft,
            CourseId = courseId, PageNumber = 2, PageSize = 5
        };
        var result = new TeacherPracticeSearchResult(
            new List<TeacherPracticeListItem> { item }, 12, 2, 5);

        var model = new TeacherPracticeIndexViewModel(query, result);

        Assert.Same(query, model.Query);
        Assert.Same(result, model.Result);
        Assert.Equal("Grammar", model.Query.Search);
        Assert.Equal(PracticeStatus.Draft, model.Query.Status);
        Assert.Equal(courseId, model.Query.CourseId);
        Assert.Equal(2, model.Query.PageNumber);
        Assert.Equal(5, model.Query.PageSize);
        Assert.Equal(item.Id, Assert.Single(model.Result.Items).Id);
        Assert.Equal(12, model.Result.TotalCount);
        Assert.Equal(2, model.Result.PageNumber);
        Assert.Equal(5, model.Result.PageSize);
        Assert.Equal(3, model.Result.TotalPages);
    }

    [Fact]
    public void Constructor_NullQuery_ThrowsArgumentNullException()
    {
        var result = EmptyResult();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TeacherPracticeIndexViewModel(null!, result));

        Assert.Equal("query", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullResult_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TeacherPracticeIndexViewModel(new TeacherPracticeSearchRequest(), null!));

        Assert.Equal("result", exception.ParamName);
    }

    [Fact]
    public void NewModel_HasNonNullEmptyCourseOptions()
    {
        var model = new TeacherPracticeIndexViewModel(
            new TeacherPracticeSearchRequest(), EmptyResult());

        Assert.NotNull(model.CourseOptions);
        Assert.Empty(model.CourseOptions);
    }

    [Fact]
    public void CourseOptions_KeepServerProvidedLabelsIdsAndSelection()
    {
        var selectedId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var query = new TeacherPracticeSearchRequest { CourseId = selectedId };
        var model = new TeacherPracticeIndexViewModel(query, EmptyResult());
        var options = new List<SelectListItem>
        {
            new SelectListItem("Selected course", selectedId.ToString(), true),
            new SelectListItem("Another course", otherId.ToString(), false)
        };

        model.CourseOptions = options;

        Assert.Equal(2, model.CourseOptions.Count);
        Assert.Equal("Selected course", model.CourseOptions[0].Text);
        Assert.Equal(selectedId.ToString(), model.CourseOptions[0].Value);
        Assert.True(model.CourseOptions[0].Selected);
        Assert.Equal("Another course", model.CourseOptions[1].Text);
        Assert.Equal(otherId.ToString(), model.CourseOptions[1].Value);
        Assert.False(model.CourseOptions[1].Selected);
        Assert.Equal(selectedId, model.Query.CourseId);
    }

    [Fact]
    public void SeparateModels_DoNotShareAssignedCourseOptions()
    {
        var first = new TeacherPracticeIndexViewModel(
            new TeacherPracticeSearchRequest(), EmptyResult());
        var second = new TeacherPracticeIndexViewModel(
            new TeacherPracticeSearchRequest(), EmptyResult());

        first.CourseOptions = new List<SelectListItem>
        {
            new SelectListItem("First teacher course", Guid.NewGuid().ToString())
        };

        Assert.Single(first.CourseOptions);
        Assert.Empty(second.CourseOptions);
        Assert.Null(second.Query.CourseId);
    }

    [Fact]
    public void Constructor_EmptyResult_IsValidForTeacherWithoutPractices()
    {
        var model = new TeacherPracticeIndexViewModel(
            new TeacherPracticeSearchRequest(), EmptyResult());

        Assert.NotNull(model.Result.Items);
        Assert.Empty(model.Result.Items);
        Assert.Equal(0, model.Result.TotalCount);
        Assert.Equal(0, model.Result.TotalPages);
    }

    [Fact]
    public void Constructor_EmptyPageWithMatches_DoesNotDiscardTotalsOrRequestedPage()
    {
        var query = new TeacherPracticeSearchRequest { PageNumber = 9, PageSize = 10 };
        var result = new TeacherPracticeSearchResult(
            Array.Empty<TeacherPracticeListItem>(), 21, 9, 10);

        var model = new TeacherPracticeIndexViewModel(query, result);

        Assert.Empty(model.Result.Items);
        Assert.Equal(21, model.Result.TotalCount);
        Assert.Equal(3, model.Result.TotalPages);
        Assert.Equal(9, model.Result.PageNumber);
        Assert.Equal(9, model.Query.PageNumber);
    }

    [Fact]
    public void Constructor_DoesNotNormalizeOrModifyCallerQuery()
    {
        var query = new TeacherPracticeSearchRequest
        {
            Search = "  Grammar  ", Status = PracticeStatus.Unpublished,
            CourseId = Guid.NewGuid(), PageNumber = 2, PageSize = 20
        };
        var courseId = query.CourseId;
        var result = new TeacherPracticeSearchResult(
            Array.Empty<TeacherPracticeListItem>(), 0, 2, 20);

        var model = new TeacherPracticeIndexViewModel(query, result);

        Assert.Same(query, model.Query);
        Assert.Equal("  Grammar  ", query.Search);
        Assert.Equal(PracticeStatus.Unpublished, query.Status);
        Assert.Equal(courseId, query.CourseId);
        Assert.Equal(2, query.PageNumber);
        Assert.Equal(20, query.PageSize);
    }

    private static TeacherPracticeSearchResult EmptyResult()
    {
        return new TeacherPracticeSearchResult(
            Array.Empty<TeacherPracticeListItem>(), 0, 1, 10);
    }
}
