using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Learning.Content;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Learning;

public sealed class CourseContentHandlerTests
{
    [Theory]
    [InlineData(ContentKind.Module, nameof(ContentInput.Visibility))]
    [InlineData(ContentKind.Lesson, nameof(ContentInput.Status))]
    public async Task NamedContent_RequiresTitleAndValidOwnStateBeforeTransaction(ContentKind kind, string stateField)
    {
        ICourseContentHandler handler = kind == ContentKind.Module
            ? new ModuleCourseContentHandler(null!) : new LessonCourseContentHandler(null!);
        var wrapper = new MemoryHandler(handler) { ParentMatches = true };
        var input = new ContentInput { Title = "  ", Visibility = (ModuleVisibility)999, Status = (LessonStatus)999 };
        var service = Service(new AccessStub(), wrapper);

        var result = await service.SaveContentAsync(Guid.NewGuid(), Guid.NewGuid(), kind, Guid.NewGuid(), null, input);

        Assert.False(result.Success);
        Assert.Equal(LearningAccessResult.Allowed, result.Access);
        Assert.Equal([nameof(ContentInput.Title), stateField], result.Errors.Keys);
        Assert.Equal("Tên không được để trống.", Assert.Single(result.Errors[nameof(ContentInput.Title)]));
        Assert.Equal("", input.Title);
        Assert.False(wrapper.SaveCalled);
    }

    [Fact]
    public async Task ResourceValidation_PreservesBothErrorsForUnknownResourceTypeAndAllowsUntitledText()
    {
        var handler = new ResourceCourseContentHandler(null!);
        var wrapper = new MemoryHandler(handler) { ParentMatches = true };
        var input = new ContentInput { ResourceType = (LessonResourceType)999, ResourceUrl = " javascript:bad " };
        var result = await Service(new AccessStub(), wrapper).SaveContentAsync(
            Guid.NewGuid(), Guid.NewGuid(), ContentKind.Resource, Guid.NewGuid(), null, input);

        Assert.Equal([nameof(ContentInput.ResourceUrl), nameof(ContentInput.ResourceType)], result.Errors.Keys);
        Assert.Equal("javascript:bad", input.ResourceUrl);
        Assert.False(wrapper.SaveCalled);
        var errors = new Dictionary<string, string[]>();
        handler.Validate(new ContentInput { ResourceType = LessonResourceType.Text, ContentText = "Content" }, errors);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ParentMismatchAndDeniedActor_PrecedeInputValidation()
    {
        var handler = new MemoryHandler(new ModuleCourseContentHandler(null!));
        var input = new ContentInput { Title = " untrimmed " };
        var courseId = Guid.NewGuid();
        var missing = await Service(new AccessStub(), handler).SaveContentAsync(
            Guid.NewGuid(), courseId, handler.Kind, courseId, null, input);
        Assert.Equal(LearningAccessResult.NotFound, missing.Access);
        Assert.Equal(" untrimmed ", input.Title);
        Assert.False(handler.ValidationCalled);

        handler.ParentMatches = true;
        var denied = await Service(new AccessStub(LearningAccessResult.Forbidden), handler).SaveContentAsync(
            Guid.NewGuid(), courseId, handler.Kind, courseId, null, input);
        Assert.Equal(LearningAccessResult.Forbidden, denied.Access);
        Assert.False(handler.ValidationCalled);
        Assert.Equal(" untrimmed ", input.Title);
    }

    [Fact]
    public async Task RegisteredExtensionKind_IsDispatchedWithoutCoordinatorChanges_UnknownKindIsNotFound()
    {
        var extended = new MemoryHandler(new ModuleCourseContentHandler(null!))
        {
            RegisteredKind = (ContentKind)42, ParentMatches = true,
            ExistingInput = new() { Title = "Extension content" }
        };
        var service = Service(new AccessStub(), extended);
        using var cancellation = new CancellationTokenSource();
        var courseId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var contentId = Guid.NewGuid();

        var read = await service.ContentInputAsync(Guid.NewGuid(), courseId, extended.Kind, parentId, contentId, cancellation.Token);

        Assert.Equal(LearningAccessResult.Allowed, read.Access);
        Assert.Same(extended.ExistingInput, read.Value);
        Assert.Equal((parentId, contentId, cancellation.Token), extended.ReadArguments);
        var unknown = await service.ContentInputAsync(Guid.NewGuid(), courseId, (ContentKind)43, parentId, null);
        Assert.Equal(LearningAccessResult.NotFound, unknown.Access);
    }

    [Fact]
    public async Task ModuleParentMustBeTheOwnedCourse_WithoutAQuery()
    {
        var handler = new ModuleCourseContentHandler(null!);
        var courseId = Guid.NewGuid();
        Assert.True(await handler.ParentMatchesAsync(courseId, courseId, default));
        Assert.False(await handler.ParentMatchesAsync(courseId, Guid.NewGuid(), default));
        Assert.True(handler.AffectsCompletion);
        Assert.True(new LessonCourseContentHandler(null!).AffectsCompletion);
        Assert.False(new ResourceCourseContentHandler(null!).AffectsCompletion);
    }

    private static CourseAuthoringService Service(ILearningAccessService access, ICourseContentHandler handler) =>
        new(null!, access, null!, null!, null!, new([handler]), null!);

    private sealed class AccessStub(LearningAccessResult result = LearningAccessResult.Allowed) : ILearningAccessService
    {
        public Task<LearningAccessResult> CheckAccessAsync(Guid userId, LearningResourceType resourceType, Guid resourceId,
            LearningOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class MemoryHandler(ICourseContentHandler validation) : ICourseContentHandler
    {
        public ContentKind? RegisteredKind { get; init; }
        public ContentKind Kind => RegisteredKind ?? validation.Kind;
        public bool AffectsCompletion => validation.AffectsCompletion;
        public bool ParentMatches { get; set; }
        public ContentInput? ExistingInput { get; init; }
        public bool ValidationCalled { get; private set; }
        public bool SaveCalled { get; private set; }
        public (Guid Parent, Guid Id, CancellationToken Cancellation) ReadArguments { get; private set; }
        public Task<bool> ParentMatchesAsync(Guid courseId, Guid parentId, CancellationToken ct) => Task.FromResult(ParentMatches);
        public Task<ContentInput?> ReadAsync(Guid parentId, Guid id, CancellationToken ct)
        {
            ReadArguments = (parentId, id, ct);
            return Task.FromResult(ExistingInput);
        }
        public void Validate(ContentInput input, IDictionary<string, string[]> errors)
        {
            ValidationCalled = true;
            validation.Validate(input, errors);
        }
        public Task<WriteOutcome> StageSaveAsync(Guid courseId, Guid parentId, Guid? id, ContentInput input, CancellationToken ct)
        {
            SaveCalled = true;
            throw new InvalidOperationException("Invalid input must be rejected before a save is staged.");
        }
        public Task<IReadOnlyList<CourseContentOrderEntry>> LoadOrderEntriesAsync(Guid parentId, CancellationToken ct) => throw new NotSupportedException();
    }
}
