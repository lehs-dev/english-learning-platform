using System.ComponentModel.DataAnnotations;
using EnglishLearningPlatform.Application.Practice;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Practice;

public sealed class CreatePracticeRequestTests
{
    [Fact]
    public void NewRequest_HasEmptyTitleAndOptionalFieldsUnset()
    {
        var request = new CreatePracticeRequest();

        Assert.Equal(string.Empty, request.Title);
        Assert.Null(request.Description);
        Assert.Null(request.LessonId);
        Assert.Null(request.ModuleId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Validate_ValidDraftWithOptionalParent_ReturnsNoErrors(
        bool hasLesson, bool hasModule)
    {
        // Draft được phép chưa có parent, hoặc chọn một Lesson/Module.
        // Ownership và parent tồn tại phải được kiểm tra ở service.
        var request = new CreatePracticeRequest
        {
            Title = "Grammar practice",
            Description = "Choose the correct answers.",
            LessonId = hasLesson ? Guid.NewGuid() : null,
            ModuleId = hasModule ? Guid.NewGuid() : null
        };

        var errors = Validate(request);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Validate_BlankTitle_ReturnsTitleError(string? title)
    {
        // null có thể đến từ dữ liệu gửi lên dù property là non-nullable.
        var request = new CreatePracticeRequest { Title = title! };

        var errors = Validate(request);

        var error = Assert.Single(errors);
        Assert.Contains(nameof(CreatePracticeRequest.Title), error.MemberNames);
        Assert.False(string.IsNullOrWhiteSpace(error.ErrorMessage));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(199)]
    [InlineData(200)]
    public void Validate_TitleWithinLimit_ReturnsNoErrors(int length)
    {
        var request = new CreatePracticeRequest
        {
            Title = new string('a', length)
        };

        var errors = Validate(request);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(201)]
    [InlineData(4000)]
    public void Validate_TitleOverLimit_ReturnsTitleError(int length)
    {
        var request = new CreatePracticeRequest
        {
            Title = new string('a', length)
        };

        var errors = Validate(request);

        var error = Assert.Single(errors);
        Assert.Contains(nameof(CreatePracticeRequest.Title), error.MemberNames);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_OptionalDescription_ReturnsNoErrors(string? description)
    {
        var request = new CreatePracticeRequest
        {
            Title = "Vocabulary practice",
            Description = description
        };

        var errors = Validate(request);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3999)]
    [InlineData(4000)]
    public void Validate_DescriptionWithinLimit_ReturnsNoErrors(int length)
    {
        var request = new CreatePracticeRequest
        {
            Title = "Reading practice",
            Description = new string('a', length)
        };

        var errors = Validate(request);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(4001)]
    [InlineData(5000)]
    public void Validate_DescriptionOverLimit_ReturnsDescriptionError(int length)
    {
        var request = new CreatePracticeRequest
        {
            Title = "Reading practice",
            Description = new string('a', length)
        };

        var errors = Validate(request);

        var error = Assert.Single(errors);
        Assert.Contains(nameof(CreatePracticeRequest.Description), error.MemberNames);
    }

    [Fact]
    public void Validate_TitleAndDescriptionOverLimits_ReturnsBothFieldErrors()
    {
        var request = new CreatePracticeRequest
        {
            Title = new string('a', 201),
            Description = new string('b', 4001)
        };

        var errors = Validate(request);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, error =>
            error.MemberNames.Contains(nameof(CreatePracticeRequest.Title)));
        Assert.Contains(errors, error =>
            error.MemberNames.Contains(nameof(CreatePracticeRequest.Description)));
    }

    [Fact]
    public void Validate_DoesNotChangeRequestValues()
    {
        var lessonId = Guid.NewGuid();
        var request = new CreatePracticeRequest
        {
            Title = "  Grammar practice  ",
            Description = "  Description  ",
            LessonId = lessonId
        };

        var errors = Validate(request);

        Assert.Empty(errors);
        Assert.Equal("  Grammar practice  ", request.Title);
        Assert.Equal("  Description  ", request.Description);
        Assert.Equal(lessonId, request.LessonId);
        Assert.Null(request.ModuleId);
    }

    private static List<ValidationResult> Validate(CreatePracticeRequest request)
    {
        // Chạy DataAnnotations giống cách service sẽ kiểm tra đầu vào.
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            errors,
            validateAllProperties: true);

        return errors;
    }
}
