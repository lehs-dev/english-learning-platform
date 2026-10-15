using EnglishLearningPlatform.Application.Practice;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Practice;

public sealed class CreatePracticeValidatorTests
{
    private readonly ICreatePracticeValidator _validator = new CreatePracticeValidator();

    [Fact]
    public void Validate_NullRequest_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => _validator.Validate(null!));

        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public void Validate_DefaultRequest_ReturnsOnlyTitleError()
    {
        var errors = _validator.Validate(new CreatePracticeRequest());

        Assert.Single(errors);
        AssertFieldError(errors, nameof(CreatePracticeRequest.Title));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Validate_ValidDraftParentChoice_ReturnsNoErrors(
        bool hasLesson, bool hasModule)
    {
        // TBR16: Draft được phép chưa có parent hoặc có đúng một parent.
        // ID ở đây chưa chứng minh parent tồn tại/thuộc Teacher; service sẽ kiểm tra.
        var request = new CreatePracticeRequest
        {
            Title = "Grammar practice",
            Description = "Choose the correct answers.",
            LessonId = hasLesson ? Guid.NewGuid() : null,
            ModuleId = hasModule ? Guid.NewGuid() : null
        };

        var errors = _validator.Validate(request);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Validate_BlankTitle_ReturnsTitleError(string? title)
    {
        var request = new CreatePracticeRequest { Title = title! };

        var errors = _validator.Validate(request);

        Assert.Single(errors);
        AssertFieldError(errors, nameof(CreatePracticeRequest.Title));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(199)]
    [InlineData(200)]
    public void Validate_TitleWithinLimit_ReturnsNoErrors(int length)
    {
        var request = new CreatePracticeRequest { Title = new string('a', length) };

        var errors = _validator.Validate(request);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(201)]
    [InlineData(4000)]
    public void Validate_TitleOverLimit_ReturnsTitleError(int length)
    {
        var request = new CreatePracticeRequest { Title = new string('a', length) };

        var errors = _validator.Validate(request);

        Assert.Single(errors);
        AssertFieldError(errors, nameof(CreatePracticeRequest.Title));
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

        var errors = _validator.Validate(request);

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

        var errors = _validator.Validate(request);

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

        var errors = _validator.Validate(request);

        Assert.Single(errors);
        AssertFieldError(errors, nameof(CreatePracticeRequest.Description));
    }

    [Fact]
    public void Validate_BothParentsSelected_ReturnsErrorForEachParent()
    {
        var request = new CreatePracticeRequest
        {
            Title = "Grammar practice",
            LessonId = Guid.NewGuid(),
            ModuleId = Guid.NewGuid()
        };

        var errors = _validator.Validate(request);

        Assert.Equal(2, errors.Count);
        AssertFieldError(errors, nameof(CreatePracticeRequest.LessonId));
        AssertFieldError(errors, nameof(CreatePracticeRequest.ModuleId));
        Assert.Single(errors[nameof(CreatePracticeRequest.LessonId)]);
        Assert.Single(errors[nameof(CreatePracticeRequest.ModuleId)]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_SingleEmptyParentId_ReturnsErrorForThatParent(bool isLesson)
    {
        var request = new CreatePracticeRequest
        {
            Title = "Grammar practice",
            LessonId = isLesson ? Guid.Empty : null,
            ModuleId = isLesson ? null : Guid.Empty
        };
        var field = isLesson
            ? nameof(CreatePracticeRequest.LessonId)
            : nameof(CreatePracticeRequest.ModuleId);

        var errors = _validator.Validate(request);

        Assert.Single(errors);
        AssertFieldError(errors, field);
        Assert.Single(errors[field]);
    }

    [Fact]
    public void Validate_BothParentIdsEmpty_PreservesBothErrorsForEachField()
    {
        var request = new CreatePracticeRequest
        {
            Title = "Grammar practice",
            LessonId = Guid.Empty,
            ModuleId = Guid.Empty
        };

        var errors = _validator.Validate(request);

        Assert.Equal(2, errors.Count);
        AssertFieldError(errors, nameof(CreatePracticeRequest.LessonId));
        AssertFieldError(errors, nameof(CreatePracticeRequest.ModuleId));
        // Mỗi parent có hai lỗi: chọn cả hai parent và ID rỗng.
        // AddError phải thêm thông báo, không ghi đè lỗi trước đó.
        Assert.Equal(2, errors[nameof(CreatePracticeRequest.LessonId)].Length);
        Assert.Equal(2, errors[nameof(CreatePracticeRequest.ModuleId)].Length);
        Assert.Equal(2, errors[nameof(CreatePracticeRequest.LessonId)].Distinct().Count());
        Assert.Equal(2, errors[nameof(CreatePracticeRequest.ModuleId)].Distinct().Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_BothParentsAndOneEmptyId_PreservesAllParentErrors(bool emptyLesson)
    {
        var request = new CreatePracticeRequest
        {
            Title = "Grammar practice",
            LessonId = emptyLesson ? Guid.Empty : Guid.NewGuid(),
            ModuleId = emptyLesson ? Guid.NewGuid() : Guid.Empty
        };
        var emptyField = emptyLesson
            ? nameof(CreatePracticeRequest.LessonId)
            : nameof(CreatePracticeRequest.ModuleId);
        var otherField = emptyLesson
            ? nameof(CreatePracticeRequest.ModuleId)
            : nameof(CreatePracticeRequest.LessonId);

        var errors = _validator.Validate(request);

        Assert.Equal(2, errors.Count);
        AssertFieldError(errors, emptyField);
        AssertFieldError(errors, otherField);
        Assert.Equal(2, errors[emptyField].Length);
        Assert.Single(errors[otherField]);
    }

    [Fact]
    public void Validate_AllFieldsInvalid_ReturnsAllFieldErrors()
    {
        var request = new CreatePracticeRequest
        {
            Title = new string('a', 201),
            Description = new string('b', 4001),
            LessonId = Guid.Empty,
            ModuleId = Guid.Empty
        };

        var errors = _validator.Validate(request);

        Assert.Equal(4, errors.Count);
        AssertFieldError(errors, nameof(CreatePracticeRequest.Title));
        AssertFieldError(errors, nameof(CreatePracticeRequest.Description));
        AssertFieldError(errors, nameof(CreatePracticeRequest.LessonId));
        AssertFieldError(errors, nameof(CreatePracticeRequest.ModuleId));
        Assert.Single(errors[nameof(CreatePracticeRequest.Title)]);
        Assert.Single(errors[nameof(CreatePracticeRequest.Description)]);
        Assert.Equal(2, errors[nameof(CreatePracticeRequest.LessonId)].Length);
        Assert.Equal(2, errors[nameof(CreatePracticeRequest.ModuleId)].Length);
    }

    [Fact]
    public void Validate_ValidRequest_DoesNotChangeRequestValues()
    {
        var lessonId = Guid.NewGuid();
        var request = new CreatePracticeRequest
        {
            Title = "  Grammar practice  ",
            Description = "  Description  ",
            LessonId = lessonId
        };

        var errors = _validator.Validate(request);

        Assert.Empty(errors);
        Assert.Equal("  Grammar practice  ", request.Title);
        Assert.Equal("  Description  ", request.Description);
        Assert.Equal(lessonId, request.LessonId);
        Assert.Null(request.ModuleId);
    }

    [Fact]
    public void Validate_InvalidRequest_DoesNotChangeRequestValues()
    {
        var moduleId = Guid.NewGuid();
        var description = new string('b', 4001);
        var request = new CreatePracticeRequest
        {
            Title = null!,
            Description = description,
            LessonId = Guid.Empty,
            ModuleId = moduleId
        };

        var errors = _validator.Validate(request);

        Assert.Equal(4, errors.Count);
        Assert.Null(request.Title);
        Assert.Equal(description, request.Description);
        Assert.Equal(Guid.Empty, request.LessonId);
        Assert.Equal(moduleId, request.ModuleId);
    }

    [Fact]
    public void Validate_RepeatedCalls_DoNotShareErrorCollections()
    {
        var request = new CreatePracticeRequest();
        var firstErrors = _validator.Validate(request);

        request.Title = "Grammar practice";
        var secondErrors = _validator.Validate(request);

        // Kết quả lần gọi sau không được xóa hoặc thay đổi lỗi lần gọi trước.
        Assert.Empty(secondErrors);
        Assert.Single(firstErrors);
        AssertFieldError(firstErrors, nameof(CreatePracticeRequest.Title));
        Assert.NotSame(firstErrors, secondErrors);
    }

    private static void AssertFieldError(
        IReadOnlyDictionary<string, string[]> errors, string field)
    {
        Assert.True(errors.ContainsKey(field), $"Missing error for {field}.");
        Assert.NotEmpty(errors[field]);
        Assert.All(errors[field], message =>
            Assert.False(string.IsNullOrWhiteSpace(message)));
    }
}
