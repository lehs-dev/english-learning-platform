using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Web.Models.Practice;
using Microsoft.AspNetCore.Mvc.Rendering;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Project này đã tham chiếu Web nên có thể kiểm tra view model trực tiếp.
// Các test model không dùng factory, không mở SQL/Docker và không sửa database.
public sealed class CreatePracticeViewModelTests
{
    [Fact]
    public void NewModel_HasInputAndEmptyOptionalContextAndOptions()
    {
        var model = new CreatePracticeViewModel();

        Assert.NotNull(model.Input);
        Assert.Equal(string.Empty, model.Input.Title);
        Assert.Null(model.Input.Description);
        Assert.Null(model.Input.LessonId);
        Assert.Null(model.Input.ModuleId);
        Assert.Null(model.CourseId);
        Assert.Null(model.CourseTitle);
        Assert.NotNull(model.CourseOptions);
        Assert.NotNull(model.ModuleOptions);
        Assert.NotNull(model.LessonOptions);
        Assert.Empty(model.CourseOptions);
        Assert.Empty(model.ModuleOptions);
        Assert.Empty(model.LessonOptions);
    }

    [Fact]
    public void SeparateModels_DoNotShareInputOrAssignedOptions()
    {
        var first = new CreatePracticeViewModel();
        var second = new CreatePracticeViewModel();

        first.Input.Title = "First draft";
        first.CourseId = Guid.NewGuid();
        first.CourseTitle = "First course";
        first.CourseOptions = new List<SelectListItem> { new SelectListItem("Course", "1") };
        first.ModuleOptions = new List<SelectListItem> { new SelectListItem("Module", "2") };
        first.LessonOptions = new List<SelectListItem> { new SelectListItem("Lesson", "3") };

        Assert.NotSame(first.Input, second.Input);
        Assert.Equal(string.Empty, second.Input.Title);
        Assert.Null(second.CourseId);
        Assert.Null(second.CourseTitle);
        Assert.Empty(second.CourseOptions);
        Assert.Empty(second.ModuleOptions);
        Assert.Empty(second.LessonOptions);
    }

    [Fact]
    public void Model_CanKeepPostedInputForRedisplayingValidationErrors()
    {
        var request = new CreatePracticeRequest
        {
            Title = "  Keep my draft title  ",
            Description = "  Keep my description  ",
            LessonId = Guid.NewGuid()
        };

        var model = new CreatePracticeViewModel { Input = request };

        Assert.Same(request, model.Input);
        Assert.Equal("  Keep my draft title  ", model.Input.Title);
        Assert.Equal("  Keep my description  ", model.Input.Description);
        Assert.Equal(request.LessonId, model.Input.LessonId);
        Assert.Null(model.Input.ModuleId);
    }

    [Theory]
    [InlineData("Course")]
    [InlineData("Module")]
    [InlineData("Lesson")]
    public void Options_KeepServerProvidedTextValueAndSelection(string kind)
    {
        var id = Guid.NewGuid().ToString();
        var item = new SelectListItem { Text = "Server-provided label", Value = id, Selected = true };
        var options = new List<SelectListItem> { item };
        var model = new CreatePracticeViewModel();
        IReadOnlyList<SelectListItem> actual;

        switch (kind)
        {
            case "Course": model.CourseOptions = options; actual = model.CourseOptions; break;
            case "Module": model.ModuleOptions = options; actual = model.ModuleOptions; break;
            case "Lesson": model.LessonOptions = options; actual = model.LessonOptions; break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var savedItem = Assert.Single(actual);
        Assert.Same(item, savedItem);
        Assert.Equal("Server-provided label", savedItem.Text);
        Assert.Equal(id, savedItem.Value);
        Assert.True(savedItem.Selected);
    }

    [Fact]
    public void CourseContext_DoesNotAutomaticallyAttachDraftToAParent()
    {
        var courseId = Guid.NewGuid();
        var model = new CreatePracticeViewModel
        {
            CourseId = courseId,
            CourseTitle = "Selected course"
        };

        Assert.Equal(courseId, model.CourseId);
        Assert.Null(model.Input.LessonId);
        Assert.Null(model.Input.ModuleId);
    }
}
