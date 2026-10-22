using System.Reflection;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Learning;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Learning;

public sealed class CourseFacadeTests
{
    [Fact]
    public void CompositeFacade_ForwardsEveryExistingMethodAndCancellationTokenToTheSpecializedService()
    {
        var catalog = DispatchProxy.Create<ICourseCatalogService, RecordingProxy>();
        var students = DispatchProxy.Create<IStudentLearningCourseService, RecordingProxy>();
        var authoring = DispatchProxy.Create<ICourseAuthoringService, RecordingProxy>();
        var catalogCalls = (RecordingProxy)catalog;
        var studentCalls = (RecordingProxy)students;
        var authoringCalls = (RecordingProxy)authoring;
        var facade = new CourseService(catalog, students, authoring);
        var actor = Guid.NewGuid();
        var id = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var contentId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var ct = cancellation.Token;
        var query = new CatalogQuery();
        var courseInput = new CourseInput();
        var contentInput = new ContentInput();
        Guid[] order = [contentId];

        Forward(catalogCalls, nameof(facade.CatalogAsync), () => facade.CatalogAsync(query, ct), query, ct, null);
        Guid[] excludedCourses = [id];
        Forward(catalogCalls, nameof(facade.CatalogAsync), () => facade.CatalogAsync(query, ct, excludedCourses), query, ct, excludedCourses);
        Forward(catalogCalls, nameof(facade.PublicDetailAsync), () => facade.PublicDetailAsync(id, actor, ct), id, actor, ct);
        Forward(studentCalls, nameof(facade.MyCoursesAsync), () => facade.MyCoursesAsync(actor, true, ct), actor, true, ct);
        Forward(studentCalls, nameof(facade.OpenCourseAsync), () => facade.OpenCourseAsync(actor, id, true, ct), actor, id, true, ct);
        Forward(studentCalls, nameof(facade.OpenLessonAsync), () => facade.OpenLessonAsync(actor, id, ct), actor, id, ct);
        Forward(studentCalls, nameof(facade.OpenModuleAsync), () => facade.OpenModuleAsync(actor, id, ct), actor, id, ct);
        Forward(studentCalls, nameof(facade.OpenResourceAsync), () => facade.OpenResourceAsync(actor, id, ct), actor, id, ct);
        Forward(studentCalls, nameof(facade.SetLessonCompletedAsync), () => facade.SetLessonCompletedAsync(actor, id, true, ct), actor, id, true, ct);
        Forward(studentCalls, nameof(facade.EnrollFreeAsync), () => facade.EnrollFreeAsync(actor, id, ct), actor, id, ct);
        Forward(authoringCalls, nameof(facade.ModuleCourseAsync), () => facade.ModuleCourseAsync(actor, id, ct), actor, id, ct);
        Forward(authoringCalls, nameof(facade.AuthoringResourcesAsync), () => facade.AuthoringResourcesAsync(actor, id, ct), actor, id, ct);
        Forward(authoringCalls, nameof(facade.CourseInputAsync), () => facade.CourseInputAsync(actor, id, ct), actor, id, ct);
        Forward(authoringCalls, nameof(facade.SaveCourseAsync), () => facade.SaveCourseAsync(actor, id, courseInput, ct), actor, id, courseInput, ct);
        Forward(authoringCalls, nameof(facade.ContentInputAsync), () => facade.ContentInputAsync(actor, id, ContentKind.Lesson, parent, contentId, ct), actor, id, ContentKind.Lesson, parent, contentId, ct);
        Forward(authoringCalls, nameof(facade.SaveContentAsync), () => facade.SaveContentAsync(actor, id, ContentKind.Lesson, parent, contentId, contentInput, ct), actor, id, ContentKind.Lesson, parent, contentId, contentInput, ct);
        Forward(authoringCalls, nameof(facade.DeleteResourceAsync), () => facade.DeleteResourceAsync(actor, id, parent, contentId, ct), actor, id, parent, contentId, ct);
        Forward(authoringCalls, nameof(facade.ReorderAsync), () => facade.ReorderAsync(actor, id, ContentKind.Lesson, parent, order, ct), actor, id, ContentKind.Lesson, parent, order, ct);
        Forward(authoringCalls, nameof(facade.ChangeStatusAsync), () => facade.ChangeStatusAsync(actor, id, CourseStatus.Published, ct), actor, id, CourseStatus.Published, ct);
    }

    private static void Forward<T>(RecordingProxy target, string method, Func<Task<T>> invoke, params object?[] arguments)
    {
        var result = Task.FromResult(default(T)!);
        target.Result = result;
        Assert.Same(result, invoke());
        Assert.Equal(method, target.Method);
        Assert.Equal(arguments, target.Arguments);
    }

    public class RecordingProxy : DispatchProxy
    {
        public object? Result { get; set; }
        public string? Method { get; private set; }
        public object?[]? Arguments { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Method = targetMethod!.Name;
            Arguments = args;
            return Result;
        }
    }
}
