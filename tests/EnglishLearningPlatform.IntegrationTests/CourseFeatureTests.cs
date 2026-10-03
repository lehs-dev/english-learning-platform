using System.Net;
using System.Text.Json;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class CourseFeatureTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task TeacherForms_EndToEndPublishPreviewAndOtherOwnerMutationDenied()
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var otherOwner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory); using var login = await IdentityTestHelpers.LoginAsync(client, owner);
        using var mine = await client.GetAsync("/TeacherCourses"); Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        using var home = await client.GetAsync("/"); Assert.Contains("href=\"/TeacherCourses\"", await home.Content.ReadAsStringAsync());
        var token = await IdentityTestHelpers.GetTokenAsync(client, "/TeacherCourses/Edit");
        var title = "UI flow " + Guid.NewGuid().ToString("N");
        using var create = await Post("/TeacherCourses/Edit", new() { ["Input.Title"] = title, ["Input.Level"] = "1", ["Input.Price"] = "0" });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Guid courseId;
        using (var scope = factory.Services.CreateScope()) courseId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Courses.Where(c => c.Title == title).Select(c => c.Id).SingleAsync();
        var modulePath = $"/TeacherCourses/Content?courseId={courseId}&kind=Module&parentId={courseId}";
        using var moduleForm = await client.GetAsync(modulePath); Assert.Equal(HttpStatusCode.OK, moduleForm.StatusCode);
        using var modulePost = await Post(modulePath, new() { ["Input.Title"] = "Module via UI", ["Input.Visibility"] = "1" }); Assert.Equal(HttpStatusCode.Redirect, modulePost.StatusCode);
        Guid moduleId;
        using (var scope = factory.Services.CreateScope()) moduleId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Modules.Where(m => m.CourseId == courseId).Select(m => m.Id).SingleAsync();
        var lessonPath = $"/TeacherCourses/Content?courseId={courseId}&kind=Lesson&parentId={moduleId}";
        using var lessonForm = await client.GetAsync(lessonPath); Assert.Equal(HttpStatusCode.OK, lessonForm.StatusCode);
        using var lessonPost = await Post(lessonPath, new() { ["Input.Title"] = "Lesson via UI", ["Input.Status"] = "1" }); Assert.Equal(HttpStatusCode.Redirect, lessonPost.StatusCode);
        Guid lessonId;
        using (var scope = factory.Services.CreateScope()) lessonId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Lessons.Where(l => l.ModuleId == moduleId).Select(l => l.Id).SingleAsync();
        var resourcePath = $"/TeacherCourses/Content?courseId={courseId}&kind=Resource&parentId={lessonId}";
        using var resourceForm = await client.GetAsync(resourcePath); Assert.Equal(HttpStatusCode.OK, resourceForm.StatusCode);
        using var invalidResource = await Post(resourcePath, new() { ["Input.ResourceType"] = "2", ["Input.ResourceUrl"] = "javascript:alert(1)" }); Assert.Equal(HttpStatusCode.OK, invalidResource.StatusCode); Assert.Contains("field-validation-error", await invalidResource.Content.ReadAsStringAsync());
        using var resourcePost = await Post(resourcePath, new() { ["Input.Title"] = "Text via UI", ["Input.ResourceType"] = "1", ["Input.ContentText"] = "<script>alert('xss')</script>UI_SECRET" }); Assert.Equal(HttpStatusCode.Redirect, resourcePost.StatusCode);
        using var publishedLesson = await Post(lessonPath + $"&id={lessonId}", new() { ["Input.Title"] = "Lesson via UI", ["Input.Status"] = "2" }); Assert.Equal(HttpStatusCode.Redirect, publishedLesson.StatusCode);
        using var publishedCourse = await Post($"/TeacherCourses/Publish/{courseId}", new()); Assert.Equal(HttpStatusCode.Redirect, publishedCourse.StatusCode);
        using var manage = await client.GetAsync($"/TeacherCourses/Manage/{courseId}"); Assert.Equal(HttpStatusCode.OK, manage.StatusCode); Assert.Contains("Text via UI", await manage.Content.ReadAsStringAsync());
        using var preview = await client.GetAsync($"/Learning/Lesson/{lessonId}"); Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.DoesNotContain("<script>alert", await preview.Content.ReadAsStringAsync()); Assert.Contains("&lt;script&gt;", await preview.Content.ReadAsStringAsync());
        using var guestClient = IdentityTestHelpers.CreateClient(factory); using var detail = await guestClient.GetAsync($"/Courses/Details/{courseId}"); Assert.DoesNotContain("UI_SECRET", await detail.Content.ReadAsStringAsync());
        using var otherClient = IdentityTestHelpers.CreateClient(factory); using var otherLogin = await IdentityTestHelpers.LoginAsync(otherClient, otherOwner);
        var otherToken = await IdentityTestHelpers.GetTokenAsync(otherClient, "/TeacherCourses/Edit");
        using var deniedMutation = await otherClient.PostAsync(lessonPath + $"&id={lessonId}", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = otherToken, ["Input.Title"] = "Hijacked", ["Input.Status"] = "1" })); Assert.Contains("/Account/AccessDenied", deniedMutation.Headers.Location!.ToString());
        using var studentClient = IdentityTestHelpers.CreateClient(factory); using var studentLogin = await IdentityTestHelpers.LoginAsync(studentClient, student);
        var enrollToken = await IdentityTestHelpers.GetTokenAsync(studentClient, $"/Courses/Details/{courseId}");
        using var enrolled = await studentClient.PostAsync($"/Courses/EnrollFree/{courseId}", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = enrollToken })); Assert.Equal(HttpStatusCode.Redirect, enrolled.StatusCode);
        using var studentLesson = await studentClient.GetAsync($"/Learning/Lesson/{lessonId}"); Assert.Equal(HttpStatusCode.OK, studentLesson.StatusCode);
        using var unpublish = await Post($"/TeacherCourses/Unpublish/{courseId}", new());
        using var archive = await Post($"/TeacherCourses/Archive/{courseId}", new());
        using var historical = await studentClient.GetAsync($"/Learning/Lesson/{lessonId}"); Assert.Equal(HttpStatusCode.OK, historical.StatusCode);

        Task<HttpResponseMessage> Post(string path, Dictionary<string,string> data) { data["__RequestVerificationToken"] = token; return client.PostAsync(path, new FormUrlEncodedContent(data)); }
    }

    [Fact]
    public async Task ConcurrentEnrollments_CreateExactlyOneEnrollment()
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student); var course = await Seed(owner.Id);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var scope = factory.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<ICourseService>().EnrollFreeAsync(student.Id, course.Id);
        }));
        Assert.All(outcomes, o => Assert.True(o.Success));
        using var scope = factory.Services.CreateScope(); Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Enrollments.CountAsync(e => e.CourseId == course.Id && e.StudentUserId == student.Id));
    }
    private async Task<Course> Seed(Guid owner, string title = "Course", bool paid = false, CourseStatus status = CourseStatus.Published)
    {
        var course = new Course { OwnerTeacherUserId = owner, Title = title, Status = status, IsPaid = paid, Price = paid ? 199000 : 0,
            Level = CourseLevel.Intermediate, Skills = [new CourseSkill { Skill = EnglishSkill.Reading }], Topics = [new CourseTopic { Topic = "Travel" }],
            Modules = [new Module { Title = "Visible module", Lessons = [new Lesson { Title = "Published title", Status = LessonStatus.Published,
                Resources = [new LessonResource { ResourceType = LessonResourceType.Text, ContentText = "SECRET_CONTENT", Title = "Text" },
                    new LessonResource { ResourceType = LessonResourceType.Link, ResourceUrl = "https://example.test/SECRET_URL", OrderIndex = 1 }] },
                new Lesson { Title = "DRAFT_TITLE", OrderIndex = 1 }, new Lesson { Title = "HIDDEN_TITLE", Status = LessonStatus.Hidden, OrderIndex = 2 }] },
                new Module { Title = "HIDDEN_MODULE", Visibility = ModuleVisibility.Hidden, OrderIndex = 1 }] };
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Courses.Add(course); await db.SaveChangesAsync(); return course;
    }
    [Fact]
    public async Task Catalog_FiltersPaginatesAndOnlyExposesPublishedMetadata()
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var prefix = Guid.NewGuid().ToString("N");
        for (var i = 0; i < 11; i++) await Seed(owner.Id, prefix + i.ToString("D2"));
        await Seed(owner.Id, prefix + "draft", status: CourseStatus.Draft);
        await Seed(owner.Id, prefix + "unpublished", status: CourseStatus.Unpublished);
        await Seed(owner.Id, prefix + "archived", status: CourseStatus.Archived);
        var paid = await Seed(owner.Id, prefix + "paid", true);
        using var scope = factory.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<ICourseService>();
        var query = new CatalogQuery { Search = prefix, Teacher = owner.Id, Level = CourseLevel.Intermediate, Skill = EnglishSkill.Reading, Topic = "Travel", Paid = false, Page = 2 };
        var page = await service.CatalogAsync(query);
        Assert.Equal(11, page.Total); Assert.Equal(2, page.Courses.Count); Assert.All(page.Courses, c => Assert.Equal(CourseStatus.Published, c.Status));
        var first = await service.CatalogAsync(new CatalogQuery { Search = prefix, Paid = false });
        Assert.Empty(first.Courses.Select(c => c.Id).Intersect(page.Courses.Select(c => c.Id)));
        Assert.Single((await service.CatalogAsync(new CatalogQuery { Search = prefix, Paid = true })).Courses, c => c.Id == paid.Id);
        Assert.Empty((await service.CatalogAsync(new CatalogQuery { Search = prefix, Skill = EnglishSkill.Grammar })).Courses);
        Assert.Equal(1, (await service.CatalogAsync(new CatalogQuery { Search = prefix, Page = -10, Level = (CourseLevel)99 })).Query.Page);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var html = await client.GetAsync($"/Courses?Search={prefix}&Paid=false&Page=1");
        Assert.Equal(HttpStatusCode.OK, html.StatusCode);
        Assert.Contains("Page=2", await html.Content.ReadAsStringAsync());
        using var malformed = await client.GetAsync("/Courses?Level=invalid&Page=oops&Paid=oops"); Assert.Equal(HttpStatusCode.OK, malformed.StatusCode);
    }
    [Fact]
    public async Task PublicDetail_DoesNotLeakResourcesOrHiddenStructure_AndCannotGuessNonPublicIds()
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var course = await Seed(owner.Id);
        using var scope = factory.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<ICourseService>();
        var json = JsonSerializer.Serialize(await service.PublicDetailAsync(course.Id, null));
        Assert.DoesNotContain("SECRET", json); Assert.DoesNotContain("ContentText", json); Assert.DoesNotContain("ResourceUrl", json);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.GetAsync($"/Courses/Details/{course.Id}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Contains("Published title", html);
        foreach (var secret in new[] { "SECRET_CONTENT", "SECRET_URL", "DRAFT_TITLE", "HIDDEN_TITLE", "HIDDEN_MODULE" }) Assert.DoesNotContain(secret, html);
        foreach (var status in new[] { CourseStatus.Draft, CourseStatus.Unpublished, CourseStatus.Archived })
        { var hidden = await Seed(owner.Id, status: status); using var denied = await client.GetAsync($"/Courses/Details/{hidden.Id}"); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); }
    }
    [Fact]
    public async Task FreeEnrollment_IsRequiredIdempotentCsrfProtectedAndScopedToStudentAndCourse()
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var otherStudent = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var course = await Seed(owner.Id); var other = await Seed(owner.Id); var paid = await Seed(owner.Id, paid: true);
        var lesson = course.Modules.First().Lessons.First(); var resource = lesson.Resources.First();
        using var client = IdentityTestHelpers.CreateClient(factory);
        foreach (var path in new[] { $"/Learning/Lesson/{lesson.Id}", $"/Learning/Resource/{resource.Id}" })
        { using var guest = await client.GetAsync(path); Assert.Contains("/Account/Login", guest.Headers.Location!.ToString()); }
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        foreach (var path in new[] { $"/Learning/Lesson/{lesson.Id}", $"/Learning/Resource/{resource.Id}" })
        { using var denied = await client.GetAsync(path); Assert.Contains("/Account/AccessDenied", denied.Headers.Location!.ToString()); }
        using var missingCsrf = await client.PostAsync($"/Courses/EnrollFree/{course.Id}", new FormUrlEncodedContent([])); Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/Courses/Details/{course.Id}");
        for (var i = 0; i < 2; i++) { using var enrolled = await client.PostAsync($"/Courses/EnrollFree/{course.Id}", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = token, ["StudentUserId"] = otherStudent.Id.ToString() })); Assert.Contains("/Learning/Course", enrolled.Headers.Location!.ToString()); }
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var service = scope.ServiceProvider.GetRequiredService<ICourseService>();
        Assert.Equal(1, await db.Enrollments.CountAsync(e => e.CourseId == course.Id && e.StudentUserId == student.Id));
        Assert.False(await db.Enrollments.AnyAsync(e => e.CourseId == course.Id && e.StudentUserId == otherStudent.Id));
        Assert.Equal(LearningAccessResult.Forbidden, (await service.EnrollFreeAsync(student.Id, paid.Id)).Access);
        using var paidPost = await client.PostAsync($"/Courses/EnrollFree/{paid.Id}", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = token }));
        Assert.Contains("/Account/AccessDenied", paidPost.Headers.Location!.ToString());
        Assert.Equal(LearningAccessResult.Forbidden, (await service.OpenLessonAsync(student.Id, other.Modules.First().Lessons.First().Id)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.OpenResourceAsync(otherStudent.Id, resource.Id)).Access);
        using var content = await client.GetAsync($"/Learning/Lesson/{lesson.Id}"); Assert.Equal(HttpStatusCode.OK, content.StatusCode); Assert.Contains("SECRET_CONTENT", await content.Content.ReadAsStringAsync());
        using var direct = await client.GetAsync($"/Learning/Resource/{resource.Id}"); Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
        Assert.Contains("no-store", content.Headers.CacheControl!.ToString());
    }
    [Fact]
    public async Task Authoring_ValidatesOwnershipParentsPricePublishAndResourceData()
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var otherOwner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var course = await Seed(owner.Id); var otherCourse = await Seed(owner.Id);
        using var scope = factory.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<ICourseService>(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var module = course.Modules.First(); var lesson = module.Lessons.First(); var resource = lesson.Resources.First();
        Assert.Equal(LearningAccessResult.Forbidden, (await service.SaveCourseAsync(otherOwner.Id, course.Id, new() { Title = "Hijack" })).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.SaveContentAsync(otherOwner.Id, course.Id, ContentKind.Resource, lesson.Id, resource.Id, new() { ResourceType = LessonResourceType.Text, ContentText = "Hijack" })).Access);
        Assert.Equal("SECRET_CONTENT", await db.LessonResources.Where(r => r.Id == resource.Id).Select(r => r.ContentText).SingleAsync());
        Assert.Equal(LearningAccessResult.NotFound, (await service.SaveContentAsync(owner.Id, course.Id, ContentKind.Lesson, otherCourse.Modules.First().Id, null, new() { Title = "Invalid parent" })).Access);
        Assert.Equal(LearningAccessResult.NotFound, (await service.SaveContentAsync(owner.Id, course.Id, ContentKind.Resource, Guid.NewGuid(), null, new())).Access);
        foreach (var pair in new[] { (false, 1m), (true, 0m), (true, -1m), (true, 1.001m), (true, 10000000000000000m) })
            Assert.False((await service.SaveCourseAsync(owner.Id, course.Id, new() { Title = "Valid", IsPaid = pair.Item1, Price = pair.Item2 })).Success);
        Assert.False((await service.SaveCourseAsync(owner.Id, course.Id, new() { Title = "  " })).Success);
        Assert.False((await service.SaveCourseAsync(owner.Id, course.Id, new() { Title = "Valid", Skills = [(EnglishSkill)99] })).Success);
        Assert.False((await service.SaveContentAsync(owner.Id, course.Id, ContentKind.Resource, lesson.Id, resource.Id, new() { ResourceType = LessonResourceType.Link, ResourceUrl = "javascript:alert(1)" })).Success);
        var draft = await service.SaveCourseAsync(owner.Id, null, new() { Title = " New Draft " }); Assert.True(draft.Success);
        Assert.Equal(CourseStatus.Draft, await db.Courses.Where(c => c.Id == draft.Id).Select(c => c.Status).SingleAsync());
        Assert.False((await service.ChangeStatusAsync(owner.Id, draft.Id, CourseStatus.Published)).Success);
        Assert.False((await service.SaveContentAsync(owner.Id, course.Id, ContentKind.Lesson, module.Id, module.Lessons.ElementAt(1).Id, new() { Title = "Missing content", Status = LessonStatus.Published })).Success);
        Assert.True((await service.ChangeStatusAsync(owner.Id, course.Id, CourseStatus.Published)).Success);
        // Mutation endpoint requires CSRF, and owner ids/status sent in the metadata form are never bound.
        using var client = IdentityTestHelpers.CreateClient(factory); using var login = await IdentityTestHelpers.LoginAsync(client, owner);
        using var missingCsrf = await client.PostAsync($"/TeacherCourses/Publish/{course.Id}", new FormUrlEncodedContent([])); Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        var token = await IdentityTestHelpers.GetTokenAsync(client, "/TeacherCourses/Edit");
        using var created = await client.PostAsync("/TeacherCourses/Edit", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = token, ["Input.Title"] = "Overpost test", ["Input.Level"] = "1", ["Input.Price"] = "0", ["OwnerTeacherUserId"] = otherOwner.Id.ToString(), ["Status"] = "Published" }));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var saved = await db.Courses.SingleAsync(c => c.OwnerTeacherUserId == owner.Id && c.Title == "Overpost test"); Assert.Equal(CourseStatus.Draft, saved.Status);
    }
    [Fact]
    public async Task Reorder_AllThreeLevelsUsesUniqueIndexAndPreservesResourcesAndProgress()
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher); var course = await Seed(owner.Id);
        using var scope = factory.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<ICourseService>(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var module = course.Modules.First(); var lesson = module.Lessons.First();
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var enrollment = new Enrollment { CourseId = course.Id, StudentUserId = student.Id };
        var progress = new LessonProgress { Enrollment = enrollment, LessonId = lesson.Id, IsCompleted = true };
        db.LessonProgressEntries.Add(progress); await db.SaveChangesAsync();
        Assert.False((await service.ReorderAsync(owner.Id, course.Id, ContentKind.Module, course.Id, [module.Id, module.Id])).Success);
        Assert.False((await service.ReorderAsync(owner.Id, course.Id, ContentKind.Lesson, module.Id, [Guid.NewGuid()])).Success);
        Assert.True((await service.ReorderAsync(owner.Id, course.Id, ContentKind.Module, course.Id, course.Modules.Reverse().Select(m => m.Id).ToArray())).Success);
        Assert.True((await service.ReorderAsync(owner.Id, course.Id, ContentKind.Lesson, module.Id, module.Lessons.Reverse().Select(l => l.Id).ToArray())).Success);
        Assert.True((await service.ReorderAsync(owner.Id, course.Id, ContentKind.Resource, lesson.Id, lesson.Resources.Reverse().Select(r => r.Id).ToArray())).Success);
        Assert.Equal(module.Id, (await db.Modules.Where(m => m.CourseId == course.Id).OrderBy(m => m.OrderIndex).LastAsync()).Id);
        Assert.Equal(2, await db.LessonResources.CountAsync(r => r.LessonId == lesson.Id));
        Assert.True(await db.LessonProgressEntries.AnyAsync(p => p.Id == progress.Id && p.IsCompleted));
        Assert.Equal(lesson.Resources.Last().Id, (await db.LessonResources.Where(r => r.LessonId == lesson.Id).OrderBy(r => r.OrderIndex).FirstAsync()).Id);
        Assert.True((await service.DeleteResourceAsync(owner.Id, course.Id, lesson.Id, lesson.Resources.First().Id)).Success);
        Assert.False((await service.DeleteResourceAsync(owner.Id, course.Id, lesson.Id, lesson.Resources.Last().Id)).Success);
        Assert.True((await service.SaveContentAsync(owner.Id, course.Id, ContentKind.Module, course.Id, module.Id, new() { Title = module.Title, Visibility = ModuleVisibility.Hidden })).Success);
        Assert.Equal(CourseStatus.Published, await db.Courses.Where(c => c.Id == course.Id).Select(c => c.Status).SingleAsync());
    }
    [Fact]
    public async Task ExistingEnrollmentSurvivesPriceTypeAndLifecycleChanges_PaymentMustMatchOriginalOrder()
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher); var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var free = await Seed(owner.Id); var paid = await Seed(owner.Id, paid: true);
        using var scope = factory.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<ICourseService>(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await service.EnrollFreeAsync(student.Id, free.Id)).Success);
        var payment = new Payment { Order = new Order { StudentUserId = student.Id, CourseId = paid.Id, Amount = 199000 }, Amount = 199000, Status = PaymentStatus.Succeeded, Provider = "test-fixture", ProviderTransactionId = Guid.NewGuid().ToString() };
        db.Enrollments.Add(new Enrollment { CourseId = paid.Id, StudentUserId = student.Id, Payment = payment, CompletedAtUtc = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        Assert.True((await service.SaveCourseAsync(owner.Id, free.Id, new() { Title = "Free now paid", IsPaid = true, Price = 299000 })).Success);
        Assert.True((await service.SaveCourseAsync(owner.Id, paid.Id, new() { Title = "Paid changed price", IsPaid = true, Price = 399000 })).Success);
        foreach (var c in new[] { free, paid }) {
            var lessonId = c.Modules.First().Lessons.First().Id;
            Assert.Equal(LearningAccessResult.Allowed, (await service.OpenLessonAsync(student.Id, lessonId)).Access);
            Assert.True((await service.ChangeStatusAsync(owner.Id, c.Id, CourseStatus.Unpublished)).Success);
            Assert.Null(await service.PublicDetailAsync(c.Id, student.Id)); Assert.Equal(LearningAccessResult.Allowed, (await service.OpenLessonAsync(student.Id, lessonId)).Access);
            Assert.True((await service.ChangeStatusAsync(owner.Id, c.Id, CourseStatus.Archived)).Success);
            Assert.Equal(LearningAccessResult.Allowed, (await service.OpenLessonAsync(student.Id, lessonId)).Access);
            Assert.Equal(LearningAccessResult.Forbidden, await scope.ServiceProvider.GetRequiredService<ILearningAccessService>().CheckAccessAsync(student.Id, LearningResourceType.Lesson, lessonId, LearningOperation.RecordProgress));
        }
        payment.Amount = 1; await db.SaveChangesAsync();
        Assert.Equal(LearningAccessResult.Forbidden, (await service.OpenLessonAsync(student.Id, paid.Modules.First().Lessons.First().Id)).Access);
    }
    [Fact]
    public async Task InactiveAccountsAndDisabledOwnerCannotEnrollOrAuthor()
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student); var course = await Seed(owner.Id);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var service = scope.ServiceProvider.GetRequiredService<ICourseService>();
        var ownerRow = await db.Users.SingleAsync(u => u.Id == owner.Id); ownerRow.AccountStatus = AccountStatus.Disabled; await db.SaveChangesAsync();
        Assert.Equal(LearningAccessResult.Forbidden, (await service.EnrollFreeAsync(student.Id, course.Id)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.SaveCourseAsync(owner.Id, course.Id, new() { Title = "Changed" })).Access);
        ownerRow.AccountStatus = AccountStatus.Active; var studentRow = await db.Users.SingleAsync(u => u.Id == student.Id); studentRow.AccountStatus = AccountStatus.Locked; await db.SaveChangesAsync();
        Assert.Equal(LearningAccessResult.Forbidden, (await service.EnrollFreeAsync(student.Id, course.Id)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await service.OpenResourceAsync(student.Id, course.Modules.First().Lessons.First().Resources.First().Id)).Access);
    }
}
