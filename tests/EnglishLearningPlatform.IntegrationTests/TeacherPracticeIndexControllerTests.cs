using System.Security.Claims;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Web.Controllers;
using EnglishLearningPlatform.Web.Models.Practice;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Direct-controller tests only: no SQL, Docker, HTTP pipeline or Razor rendering.
// Authorization filters and actual HTML need separate HTTP tests after Index.cshtml exists.
public sealed class TeacherPracticeIndexControllerTests
{
    [Fact]
    public void Constructor_NullCourseService_ThrowsWithParameterName()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TeacherPracticesController(null!, new RejectWritesPracticeService(), new QueryServiceFake()));

        Assert.Equal("courseService", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullPracticeService_ThrowsWithParameterName()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TeacherPracticesController(new CourseServiceFake(), null!, new QueryServiceFake()));

        Assert.Equal("practiceService", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullQueryService_ThrowsWithParameterName()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TeacherPracticesController(new CourseServiceFake(), new RejectWritesPracticeService(), null!));

        Assert.Equal("practiceQueryService", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Index_MissingMalformedOrEmptyActorId_ChallengesBeforeCallingServices(string? claimValue)
    {
        var context = new TestContext(claimValue);

        var result = await context.Controller.Index(new TeacherPracticeSearchRequest(), default);

        Assert.IsType<ChallengeResult>(result);
        AssertNoServiceCalls(context);
    }

    [Fact]
    public async Task Index_InvalidIdentityTakesPrecedenceOverInvalidQuery()
    {
        var context = new TestContext(null);
        context.Controller.ModelState.AddModelError("PageNumber", "Invalid number");

        var result = await context.Controller.Index(null!, default);

        Assert.IsType<ChallengeResult>(result);
        AssertNoServiceCalls(context);
    }

    [Fact]
    public async Task Index_NullQuery_ReturnsBadRequestWithoutCallingServices()
    {
        var context = new TestContext();

        var result = await context.Controller.Index(null!, default);

        Assert.IsType<BadRequestObjectResult>(result);
        AssertNoServiceCalls(context);
    }

    [Theory]
    [InlineData("PageNumber")]
    [InlineData("PageSize")]
    [InlineData("CourseId")]
    [InlineData("Status")]
    public async Task Index_ModelBindingError_ReturnsBadRequestWithoutCallingServices(string field)
    {
        var context = new TestContext();
        // A direct action call does not run model binding, so represent its error explicitly.
        context.Controller.ModelState.AddModelError(field, "The value cannot be converted.");

        var result = await context.Controller.Index(new TeacherPracticeSearchRequest(), default);

        Assert.IsType<BadRequestObjectResult>(result);
        AssertNoServiceCalls(context);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task Index_InvalidPageNumber_ReturnsBadRequestWithoutCallingServices(int pageNumber)
    {
        var context = new TestContext();
        var query = new TeacherPracticeSearchRequest { PageNumber = pageNumber };

        var result = await context.Controller.Index(query, default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(pageNumber, query.PageNumber);
        AssertNoServiceCalls(context);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public async Task Index_InvalidPageSize_ReturnsBadRequestWithoutCallingServices(int pageSize)
    {
        var context = new TestContext();
        var query = new TeacherPracticeSearchRequest { PageSize = pageSize };

        var result = await context.Controller.Index(query, default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(pageSize, query.PageSize);
        AssertNoServiceCalls(context);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public async Task Index_UndefinedStatus_ReturnsBadRequestWithoutCallingServices(int status)
    {
        var context = new TestContext();
        var query = new TeacherPracticeSearchRequest { Status = (PracticeStatus)status };

        var result = await context.Controller.Index(query, default);

        Assert.IsType<BadRequestObjectResult>(result);
        AssertNoServiceCalls(context);
    }

    [Fact]
    public async Task Index_EmptyCourseId_ReturnsBadRequestWithoutCallingServices()
    {
        var context = new TestContext();
        var query = new TeacherPracticeSearchRequest { CourseId = Guid.Empty };

        var result = await context.Controller.Index(query, default);

        Assert.IsType<BadRequestObjectResult>(result);
        AssertNoServiceCalls(context);
    }

    [Theory]
    [InlineData(LearningAccessResult.Forbidden)]
    [InlineData(LearningAccessResult.NotFound)]
    public async Task Index_CourseServiceDeniesAccess_DoesNotCallPracticeQuery(LearningAccessResult access)
    {
        var context = new TestContext();
        // Even a denied outcome containing data must not expose that data.
        context.Courses.Outcome = new LearningOutcome<IReadOnlyList<CourseSummary>>(
            access, new[] { NewCourse(context.ActorId, "Private course") });

        var result = await context.Controller.Index(new TeacherPracticeSearchRequest(), default);

        AssertDeniedResult(access, result);
        Assert.Equal(1, context.Courses.MyCoursesCalls);
        Assert.Equal(0, context.Query.Calls);
        AssertReadOnly(context);
    }

    [Fact]
    public async Task Index_AllowedCourseOutcomeWithoutValue_ReturnsNotFoundBeforePracticeQuery()
    {
        var context = new TestContext();
        context.Courses.Outcome = new LearningOutcome<IReadOnlyList<CourseSummary>>(
            LearningAccessResult.Allowed);

        var result = await context.Controller.Index(new TeacherPracticeSearchRequest(), default);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(1, context.Courses.MyCoursesCalls);
        Assert.Equal(0, context.Query.Calls);
        AssertReadOnly(context);
    }

    [Fact]
    public async Task Index_UnknownSelectedCourse_ReturnsNotFoundBeforePracticeQuery()
    {
        var context = new TestContext();
        context.Courses.Outcome = AllowedCourses(NewCourse(context.ActorId, "Owned course"));

        var result = await context.Controller.Index(
            new TeacherPracticeSearchRequest { CourseId = Guid.NewGuid() }, default);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(0, context.Query.Calls);
        AssertReadOnly(context);
    }

    [Fact]
    public async Task Index_SelectedForeignCourseReturnedByCourseService_StillReturnsNotFound()
    {
        var context = new TestContext();
        var foreign = NewCourse(Guid.NewGuid(), "Foreign course must not be exposed");
        context.Courses.Outcome = AllowedCourses(foreign);

        var result = await context.Controller.Index(
            new TeacherPracticeSearchRequest { CourseId = foreign.Id }, default);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(0, context.Query.Calls);
        AssertReadOnly(context);
    }

    [Fact]
    public async Task Index_OwnedCourseDoesNotAuthorizeADifferentForeignCourseWithMatchingRequestedId()
    {
        var context = new TestContext();
        var own = NewCourse(context.ActorId, "Owned");
        var foreign = NewCourse(Guid.NewGuid(), "Foreign");
        context.Courses.Outcome = AllowedCourses(own, foreign);

        var result = await context.Controller.Index(
            new TeacherPracticeSearchRequest { CourseId = foreign.Id }, default);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(0, context.Query.Calls);
        AssertReadOnly(context);
    }

    [Theory]
    [InlineData(LearningAccessResult.Forbidden)]
    [InlineData(LearningAccessResult.NotFound)]
    public async Task Index_QueryServiceDeniesAccess_ReturnsDenialInsteadOfView(LearningAccessResult access)
    {
        var context = new TestContext();
        // This deliberately includes a value: Access, not a non-null Value, decides authorization.
        context.Query.Outcome = new LearningOutcome<TeacherPracticeSearchResult>(
            access, EmptySearchResult());

        var result = await context.Controller.Index(new TeacherPracticeSearchRequest(), default);

        AssertDeniedResult(access, result);
        Assert.Equal(1, context.Courses.MyCoursesCalls);
        Assert.Equal(1, context.Query.Calls);
        AssertReadOnly(context);
    }

    [Fact]
    public async Task Index_AllowedQueryOutcomeWithoutValue_ReturnsNotFound()
    {
        var context = new TestContext();
        context.Query.Outcome = new LearningOutcome<TeacherPracticeSearchResult>(LearningAccessResult.Allowed);

        var result = await context.Controller.Index(new TeacherPracticeSearchRequest(), default);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(1, context.Query.Calls);
        AssertReadOnly(context);
    }

    [Fact]
    public async Task Index_AllowedResults_ReturnsModelWithUnchangedFiltersAndServerProvidedDropdown()
    {
        var context = new TestContext();
        var first = NewCourse(context.ActorId, "Reading <advanced>");
        var selected = NewCourse(context.ActorId, "Grammar & vocabulary");
        context.Courses.Outcome = AllowedCourses(first, selected);
        var query = new TeacherPracticeSearchRequest
        {
            Search = "  grammar & vocabulary  ",
            Status = PracticeStatus.Unpublished,
            CourseId = selected.Id,
            PageNumber = 3,
            PageSize = 7
        };
        var item = new TeacherPracticeListItem
        {
            Id = Guid.NewGuid(),
            Title = "Matching draft",
            Status = PracticeStatus.Unpublished,
            QuestionCount = 5,
            CourseId = selected.Id,
            CourseTitle = selected.Title
        };
        var searchResult = new TeacherPracticeSearchResult(new[] { item }, 15, 3, 7);
        context.Query.Outcome = new LearningOutcome<TeacherPracticeSearchResult>(
            LearningAccessResult.Allowed, searchResult);

        var result = await context.Controller.Index(query, default);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<TeacherPracticeIndexViewModel>(view.Model);
        Assert.Null(view.ViewName); // MVC resolves Index from the current action; this test does not render it.
        Assert.Same(query, model.Query);
        Assert.Same(searchResult, model.Result);
        Assert.Same(item, Assert.Single(model.Result.Items));
        Assert.Equal("  grammar & vocabulary  ", model.Query.Search);
        Assert.Equal(PracticeStatus.Unpublished, model.Query.Status);
        Assert.Equal(selected.Id, model.Query.CourseId);
        Assert.Equal(3, model.Query.PageNumber);
        Assert.Equal(7, model.Query.PageSize);
        Assert.Collection(model.CourseOptions,
            option =>
            {
                Assert.Equal(first.Id.ToString(), option.Value);
                Assert.Equal(first.Title, option.Text);
                Assert.False(option.Selected);
                Assert.False(option.Disabled);
            },
            option =>
            {
                Assert.Equal(selected.Id.ToString(), option.Value);
                Assert.Equal(selected.Title, option.Text);
                Assert.True(option.Selected);
                Assert.False(option.Disabled);
            });
        Assert.Equal(1, context.Courses.MyCoursesCalls);
        Assert.Equal(1, context.Query.Calls);
        AssertReadOnly(context);
    }

    [Fact]
    public async Task Index_NoCourseFilter_DoesNotSelectCourseOrHideUnattachedDraft()
    {
        var context = new TestContext();
        context.Courses.Outcome = AllowedCourses(NewCourse(context.ActorId, "Course"));
        var draft = new TeacherPracticeListItem
        {
            Id = Guid.NewGuid(), Title = "Unattached draft", Status = PracticeStatus.Draft
        };
        context.Query.Outcome = new LearningOutcome<TeacherPracticeSearchResult>(
            LearningAccessResult.Allowed,
            new TeacherPracticeSearchResult(new[] { draft }, 1, 1, 10));

        var result = await context.Controller.Index(new TeacherPracticeSearchRequest(), default);

        var model = Assert.IsType<TeacherPracticeIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(Assert.Single(model.CourseOptions).Selected);
        Assert.Null(model.Query.CourseId);
        var actual = Assert.Single(model.Result.Items);
        Assert.Same(draft, actual);
        Assert.Null(actual.CourseId);
        Assert.Null(actual.ModuleId);
        Assert.Null(actual.LessonId);
        AssertReadOnly(context);
    }

    [Fact]
    public async Task Index_NoCoursesAndNoPractices_ReturnsAllowedEmptyView()
    {
        var context = new TestContext();

        var result = await context.Controller.Index(new TeacherPracticeSearchRequest(), default);

        var model = Assert.IsType<TeacherPracticeIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Empty(model.CourseOptions);
        Assert.Empty(model.Result.Items);
        Assert.Equal(0, model.Result.TotalCount);
        Assert.Equal(0, model.Result.TotalPages);
        Assert.Equal(1, context.Query.Calls);
        AssertReadOnly(context);
    }

    [Theory]
    [InlineData(PracticeStatus.Draft)]
    [InlineData(PracticeStatus.Published)]
    [InlineData(PracticeStatus.Unpublished)]
    public async Task Index_DefinedStatus_IsPassedUnchangedToQuery(PracticeStatus status)
    {
        var context = new TestContext();
        var query = new TeacherPracticeSearchRequest { Status = status };

        var result = await context.Controller.Index(query, default);

        Assert.IsType<ViewResult>(result);
        Assert.Same(query, context.Query.LastRequest);
        Assert.Equal(status, context.Query.LastRequest!.Status);
        AssertReadOnly(context);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    [InlineData(int.MaxValue, 100)]
    public async Task Index_ValidPaginationBoundaries_ArePassedToServiceWithoutClamping(int page, int size)
    {
        var context = new TestContext();
        var query = new TeacherPracticeSearchRequest { PageNumber = page, PageSize = size };

        var result = await context.Controller.Index(query, default);

        Assert.IsType<ViewResult>(result);
        Assert.Same(query, context.Query.LastRequest);
        Assert.Equal(page, context.Query.LastRequest!.PageNumber);
        Assert.Equal(size, context.Query.LastRequest.PageSize);
        AssertReadOnly(context);
    }

    [Fact]
    public async Task Index_UsesClaimActorAndPropagatesTokenToBothServicesInReadOrder()
    {
        var context = new TestContext();
        var course = NewCourse(context.ActorId, "Owned course");
        context.Courses.Outcome = AllowedCourses(course);
        var query = new TeacherPracticeSearchRequest { CourseId = course.Id };
        using var tokenSource = new CancellationTokenSource();

        var result = await context.Controller.Index(query, tokenSource.Token);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(context.ActorId, context.Courses.LastActorId);
        Assert.True(context.Courses.LastTeacherMode);
        Assert.Equal(tokenSource.Token, context.Courses.LastToken);
        Assert.Equal(context.ActorId, context.Query.LastActorId);
        Assert.Equal(tokenSource.Token, context.Query.LastToken);
        Assert.Same(query, context.Query.LastRequest);
        Assert.Equal(new[] { "courses", "query" }, context.CallOrder);
        AssertReadOnly(context);
    }

    [Fact]
    public async Task Index_CourseServiceCancellation_PropagatesWithoutCallingQueryOrWrites()
    {
        var context = new TestContext();
        using var tokenSource = new CancellationTokenSource();
        tokenSource.Cancel();
        context.Courses.HonorCancellation = true;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.Controller.Index(new TeacherPracticeSearchRequest(), tokenSource.Token));

        Assert.Equal(tokenSource.Token, context.Courses.LastToken);
        Assert.Equal(0, context.Query.Calls);
        AssertReadOnly(context);
    }

    private static TeacherPracticeSearchResult EmptySearchResult()
    {
        return new TeacherPracticeSearchResult(Array.Empty<TeacherPracticeListItem>(), 0, 1, 10);
    }

    private static CourseSummary NewCourse(Guid ownerId, string title)
    {
        return new CourseSummary(
            Guid.NewGuid(), ownerId, title, null, null, "Teacher", CourseLevel.Beginner,
            CourseStatus.Draft, false, 0m, Array.Empty<EnglishSkill>(), Array.Empty<string>(), 0, 0);
    }

    private static LearningOutcome<IReadOnlyList<CourseSummary>> AllowedCourses(params CourseSummary[] courses)
    {
        return new LearningOutcome<IReadOnlyList<CourseSummary>>(LearningAccessResult.Allowed, courses);
    }

    private static void AssertDeniedResult(LearningAccessResult access, IActionResult result)
    {
        if (access == LearningAccessResult.NotFound)
        {
            Assert.IsType<NotFoundResult>(result);
        }
        else
        {
            Assert.IsType<ForbidResult>(result);
        }
    }

    private static void AssertNoServiceCalls(TestContext context)
    {
        Assert.Equal(0, context.Courses.MyCoursesCalls);
        Assert.Equal(0, context.Query.Calls);
        Assert.Empty(context.CallOrder);
        AssertReadOnly(context);
    }

    private static void AssertReadOnly(TestContext context)
    {
        Assert.Equal(0, context.Practice.CreateCalls);
        Assert.Equal(0, context.Courses.UnexpectedCalls);
    }

    private sealed class TestContext
    {
        public Guid ActorId { get; } = Guid.NewGuid();
        public List<string> CallOrder { get; } = new();
        public CourseServiceFake Courses { get; } = new();
        public QueryServiceFake Query { get; } = new();
        public RejectWritesPracticeService Practice { get; } = new();
        public TeacherPracticesController Controller { get; }

        public TestContext() : this(null, useActorClaim: true) { }

        public TestContext(string? claimValue) : this(claimValue, useActorClaim: false) { }

        private TestContext(string? claimValue, bool useActorClaim)
        {
            Courses.CallOrder = CallOrder;
            Query.CallOrder = CallOrder;
            var claims = new List<Claim>();
            string? actorClaim = useActorClaim ? ActorId.ToString() : claimValue;
            if (actorClaim != null)
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, actorClaim));
            }

            Controller = new TeacherPracticesController(Courses, Practice, Query)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "DirectControllerTest"))
                    }
                }
            };
        }
    }

    private sealed class QueryServiceFake : ITeacherPracticeQueryService
    {
        public LearningOutcome<TeacherPracticeSearchResult> Outcome { get; set; } =
            new(LearningAccessResult.Allowed, EmptySearchResult());
        public int Calls { get; private set; }
        public Guid LastActorId { get; private set; }
        public TeacherPracticeSearchRequest? LastRequest { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public List<string> CallOrder { get; set; } = new();

        public Task<LearningOutcome<TeacherPracticeSearchResult>> SearchAsync(
            Guid actorUserId, TeacherPracticeSearchRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastActorId = actorUserId;
            LastRequest = request;
            LastToken = cancellationToken;
            CallOrder.Add("query");
            return Task.FromResult(Outcome);
        }
    }

    private sealed class RejectWritesPracticeService : ITeacherPracticeService
    {
        public int CreateCalls { get; private set; }

        public Task<WriteOutcome> CreateDraftAsync(
            Guid actorUserId, CreatePracticeRequest request, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            throw new InvalidOperationException("GET Index must never create a Practice.");
        }
    }

    // Only MyCoursesAsync is expected. Every other call fails, including all Course write methods.
    private sealed class CourseServiceFake : ICourseService
    {
        public LearningOutcome<IReadOnlyList<CourseSummary>> Outcome { get; set; } =
            AllowedCourses();
        public int MyCoursesCalls { get; private set; }
        public int UnexpectedCalls { get; private set; }
        public Guid LastActorId { get; private set; }
        public bool LastTeacherMode { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public bool HonorCancellation { get; set; }
        public List<string> CallOrder { get; set; } = new();

        public Task<LearningOutcome<IReadOnlyList<CourseSummary>>> MyCoursesAsync(
            Guid userId, bool teacher, CancellationToken ct = default)
        {
            MyCoursesCalls++;
            LastActorId = userId;
            LastTeacherMode = teacher;
            LastToken = ct;
            CallOrder.Add("courses");
            if (HonorCancellation)
            {
                ct.ThrowIfCancellationRequested();
            }
            return Task.FromResult(Outcome);
        }

        private Task<T> Unexpected<T>(string member)
        {
            UnexpectedCalls++;
            throw new InvalidOperationException("GET Index must not call " + member + ".");
        }

        public Task<LearningOutcome<IReadOnlyDictionary<Guid, IReadOnlyList<ResourceContent>>>> AuthoringResourcesAsync(
            Guid userId, Guid courseId, CancellationToken ct = default) =>
            Unexpected<LearningOutcome<IReadOnlyDictionary<Guid, IReadOnlyList<ResourceContent>>>>(nameof(AuthoringResourcesAsync));

        public Task<CatalogPage> CatalogAsync(CatalogQuery query, CancellationToken ct = default) =>
            Unexpected<CatalogPage>(nameof(CatalogAsync));

        public Task<CoursePage?> PublicDetailAsync(Guid id, Guid? userId, CancellationToken ct = default) =>
            Unexpected<CoursePage?>(nameof(PublicDetailAsync));

        public Task<LearningOutcome<CoursePage>> OpenCourseAsync(Guid userId, Guid id, bool manage, CancellationToken ct = default) =>
            Unexpected<LearningOutcome<CoursePage>>(nameof(OpenCourseAsync));

        public Task<LearningOutcome<LessonPage>> OpenLessonAsync(Guid userId, Guid id, CancellationToken ct = default) =>
            Unexpected<LearningOutcome<LessonPage>>(nameof(OpenLessonAsync));

        public Task<LearningOutcome<ModulePage>> OpenModuleAsync(Guid userId, Guid id, CancellationToken ct = default) =>
            Unexpected<LearningOutcome<ModulePage>>(nameof(OpenModuleAsync));

        public Task<WriteOutcome> SetLessonCompletedAsync(Guid userId, Guid id, bool completed, CancellationToken ct = default) =>
            Unexpected<WriteOutcome>(nameof(SetLessonCompletedAsync));

        public Task<LearningOutcome<ResourceContent>> OpenResourceAsync(Guid userId, Guid id, CancellationToken ct = default) =>
            Unexpected<LearningOutcome<ResourceContent>>(nameof(OpenResourceAsync));

        public Task<LearningOutcome<Guid>> ModuleCourseAsync(Guid userId, Guid id, CancellationToken ct = default) =>
            Unexpected<LearningOutcome<Guid>>(nameof(ModuleCourseAsync));

        public Task<LearningOutcome<CourseInput>> CourseInputAsync(Guid userId, Guid id, CancellationToken ct = default) =>
            Unexpected<LearningOutcome<CourseInput>>(nameof(CourseInputAsync));

        public Task<WriteOutcome> SaveCourseAsync(Guid userId, Guid? id, CourseInput input, CancellationToken ct = default) =>
            Unexpected<WriteOutcome>(nameof(SaveCourseAsync));

        public Task<LearningOutcome<ContentInput>> ContentInputAsync(
            Guid userId, Guid courseId, ContentKind kind, Guid parentId, Guid? id, CancellationToken ct = default) =>
            Unexpected<LearningOutcome<ContentInput>>(nameof(ContentInputAsync));

        public Task<WriteOutcome> SaveContentAsync(
            Guid userId, Guid courseId, ContentKind kind, Guid parentId, Guid? id, ContentInput input, CancellationToken ct = default) =>
            Unexpected<WriteOutcome>(nameof(SaveContentAsync));

        public Task<WriteOutcome> DeleteResourceAsync(
            Guid userId, Guid courseId, Guid lessonId, Guid id, CancellationToken ct = default) =>
            Unexpected<WriteOutcome>(nameof(DeleteResourceAsync));

        public Task<WriteOutcome> ReorderAsync(
            Guid userId, Guid courseId, ContentKind kind, Guid parentId, Guid[] ids, CancellationToken ct = default) =>
            Unexpected<WriteOutcome>(nameof(ReorderAsync));

        public Task<WriteOutcome> ChangeStatusAsync(Guid userId, Guid id, CourseStatus status, CancellationToken ct = default) =>
            Unexpected<WriteOutcome>(nameof(ChangeStatusAsync));

        public Task<WriteOutcome> EnrollFreeAsync(Guid userId, Guid id, CancellationToken ct = default) =>
            Unexpected<WriteOutcome>(nameof(EnrollFreeAsync));
    }
}
