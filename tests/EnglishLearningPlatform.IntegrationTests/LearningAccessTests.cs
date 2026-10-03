using System.Net;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class LearningAccessTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData(AppRoles.Teacher, true)]
    [InlineData(AppRoles.Student, false)]
    [InlineData(AppRoles.Admin, false)]
    public async Task ManageCourse_RequiresTeacherRoleAndOwnership(string role, bool canManage)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        var owner = role == AppRoles.Teacher ? user : await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var course = await CreateCourseAsync(owner.Id);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        using var response = await client.GetAsync($"/Learning/ManageCourse/{course.Id}");
        Assert.Equal(canManage ? HttpStatusCode.OK : HttpStatusCode.Redirect, response.StatusCode);
        if (!canManage) Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task OtherTeacher_CannotOpenCourseModuleLessonOrManageCourse()
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var course = await CreateCourseAsync(owner.Id);
        var module = course.Modules.Single();
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, other);
        foreach (var path in new[] { $"/Learning/Course/{course.Id}", $"/Learning/Module/{module.Id}",
                     $"/Learning/Lesson/{module.Lessons.Single().Id}", $"/Learning/ManageCourse/{course.Id}" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
        }
    }

    [Theory]
    [InlineData(CourseStatus.Published, true, true)]
    [InlineData(CourseStatus.Unpublished, true, true)]
    [InlineData(CourseStatus.Archived, true, false)]
    [InlineData(CourseStatus.Draft, false, false)]
    public async Task EnrolledStudent_AccessFollowsCourseLifecycle(CourseStatus status, bool canView, bool canRecord)
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var course = await CreateCourseAsync(owner.Id, status);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Enrollments.Add(new Enrollment { StudentUserId = student.Id, CourseId = course.Id });
        await db.SaveChangesAsync();
        var access = scope.ServiceProvider.GetRequiredService<ILearningAccessService>();
        var lessonId = course.Modules.Single().Lessons.Single().Id;
        Assert.Equal(canView ? LearningAccessResult.Allowed : LearningAccessResult.Forbidden,
            await access.CheckAccessAsync(student.Id, LearningResourceType.Lesson, lessonId, LearningOperation.ViewContent));
        Assert.Equal(canRecord ? LearningAccessResult.Allowed : LearningAccessResult.Forbidden,
            await access.CheckAccessAsync(student.Id, LearningResourceType.Lesson, lessonId, LearningOperation.RecordProgress));
    }

    [Theory]
    [InlineData(ModuleVisibility.Hidden, LessonStatus.Published)]
    [InlineData(ModuleVisibility.Visible, LessonStatus.Hidden)]
    [InlineData(ModuleVisibility.Visible, LessonStatus.Draft)]
    public async Task UnavailableLesson_IsBlockedForEnrolledStudent_ButOwnerCanPreview(
        ModuleVisibility visibility, LessonStatus status)
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var course = await CreateCourseAsync(owner.Id, visibility: visibility, lessonStatus: status);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Enrollments.Add(new Enrollment { StudentUserId = student.Id, CourseId = course.Id });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<ILearningAccessService>();
        var lessonId = course.Modules.Single().Lessons.Single().Id;
        Assert.Equal(LearningAccessResult.Forbidden,
            await service.CheckAccessAsync(student.Id, LearningResourceType.Lesson, lessonId, LearningOperation.ViewContent));
        Assert.Equal(LearningAccessResult.Allowed,
            await service.CheckAccessAsync(owner.Id, LearningResourceType.Lesson, lessonId, LearningOperation.ViewContent));
    }

    [Fact]
    public async Task UnenrolledStudent_CannotOpenPublishedContent_AndGuestMustLogin()
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var course = await CreateCourseAsync(owner.Id);
        using var client = IdentityTestHelpers.CreateClient(factory);
        var path = $"/Learning/Lesson/{course.Modules.Single().Lessons.Single().Id}";
        using var guest = await client.GetAsync(path);
        Assert.Contains("/Account/Login", guest.Headers.Location!.ToString());
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        using var response = await client.GetAsync(path);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task MissingResource_Returns404_AndAccessDeniedPageReturns403()
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        using var missing = await client.GetAsync($"/Learning/Lesson/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var denied = await client.GetAsync("/Account/AccessDenied");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private async Task<Course> CreateCourseAsync(Guid ownerId, CourseStatus status = CourseStatus.Published,
        ModuleVisibility visibility = ModuleVisibility.Visible, LessonStatus lessonStatus = LessonStatus.Published)
    {
        var course = new Course
        {
            OwnerTeacherUserId = ownerId,
            Title = "Access test",
            Status = status,
            Modules = [new Module
            {
                Title = "Module", Visibility = visibility,
                Lessons = [new Lesson { Title = "Lesson", Status = lessonStatus }]
            }]
        };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        return course;
    }
}
