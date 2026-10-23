using System.Net;
using System.Net.Http.Json;
using EnglishLearningPlatform.Application.Commerce;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Commerce;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Week 6: exercise the real MVC/forms/callback pipeline, then inspect persisted
// Learning + Commerce invariants. CheckoutTestFactory owns a separate database.
public sealed class LearningCommerceJourneyTests(CheckoutTestFactory factory) : IClassFixture<CheckoutTestFactory>
{
    [Fact]
    public async Task PaidFailureThenRetrySuccess_OpensLearningOnceAndPreservesProgressOnCallbackReplay()
    {
        var data = await SeedAsync(paid: true);
        var otherStudent = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var student = await LoginAsync(data.Student);
        using var other = await LoginAsync(otherStudent);
        using var guest = IdentityTestHelpers.CreateClient(factory);

        using var detail = await guest.GetAsync($"/Courses/Details/{data.Course.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.DoesNotContain("WEEK6_PRIVATE_CONTENT", await detail.Content.ReadAsStringAsync());
        AssertDenied(await student.GetAsync($"/Learning/Lesson/{data.First.Id}"));

        var failedOrder = await CreateOrderAsync(student, data.Course.Id, otherStudent.Id);
        await AssertRowsAsync(data, orders: 1, payments: 0, enrollments: 0);
        using var forgedReturn = await student.GetAsync($"/Checkout/Result/{failedOrder}?paid=true&status=Succeeded");
        Assert.Equal(HttpStatusCode.OK, forgedReturn.StatusCode);
        await AssertRowsAsync(data, 1, 0, 0);
        AssertDenied(await student.GetAsync($"/Learning/Lesson/{data.First.Id}"));

        using var failed = await PostAsync(student, $"/Checkout/Simulate/{failedOrder}", new() { ["scenario"] = "failed" });
        Assert.Equal(HttpStatusCode.Redirect, failed.StatusCode);
        using var failedPage = await student.GetAsync($"/Checkout/Order/{failedOrder}");
        Assert.Contains("Giao dịch thất bại", WebUtility.HtmlDecode(await failedPage.Content.ReadAsStringAsync()));
        await AssertRowsAsync(data, 1, 1, 0);
        AssertDenied(await student.GetAsync($"/Learning/Lesson/{data.First.Id}"));

        // A failed local receipt is terminal. Retry must use a new order, rather
        // than changing that receipt into a success or granting early access.
        using var contradictory = await guest.PostAsJsonAsync("/payments/webhook", await CallbackAsync(data.Student.Id, failedOrder));
        Assert.Equal(HttpStatusCode.Conflict, contradictory.StatusCode);
        var successOrder = await CreateOrderAsync(student, data.Course.Id);
        Assert.NotEqual(failedOrder, successOrder);
        Assert.Equal(successOrder, await CreateOrderAsync(student, data.Course.Id));
        var callback = await CallbackAsync(data.Student.Id, successOrder);
        using var accepted = await guest.PostAsJsonAsync("/payments/webhook", callback);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await AssertRowsAsync(data, 2, 2, 1);

        using var lesson = await student.GetAsync($"/Learning/Lesson/{data.First.Id}");
        Assert.Equal(HttpStatusCode.OK, lesson.StatusCode);
        Assert.Contains("WEEK6_PRIVATE_CONTENT", await lesson.Content.ReadAsStringAsync());
        using var privateOrder = await other.GetAsync($"/Checkout/Order/{successOrder}");
        Assert.Equal(HttpStatusCode.NotFound, privateOrder.StatusCode);
        AssertDenied(await other.GetAsync($"/Learning/Lesson/{data.First.Id}"));

        await CompleteAsync(student, data.Second.Id, true); // Learning need not be sequential.
        await AssertProgressAsync(data, 1, 2, null);
        await CompleteAsync(student, data.First.Id, true);
        var completedAt = (await ProgressAsync(data)).CompletedAtUtc;
        Assert.NotNull(completedAt);

        var replays = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => guest.PostAsJsonAsync("/payments/webhook", callback)));
        foreach (var replay in replays)
        {
            using (replay) Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        }
        await AssertRowsAsync(data, 2, 2, 1);
        await AssertProgressAsync(data, 2, 2, completedAt);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var enrollment = await db.Enrollments.AsNoTracking().Include(e => e.Payment)
            .SingleAsync(e => e.CourseId == data.Course.Id && e.StudentUserId == data.Student.Id);
        Assert.Equal(successOrder, enrollment.Payment!.OrderId);
        Assert.Equal(PaymentStatus.Succeeded, enrollment.Payment.Status);
        Assert.Equal(PaymentStatus.Failed, await db.Payments.Where(p => p.OrderId == failedOrder).Select(p => p.Status).SingleAsync());
        Assert.Equal(2, await db.LessonProgressEntries.CountAsync(p => p.EnrollmentId == enrollment.Id));
        var report = (await scope.ServiceProvider.GetRequiredService<ITeacherCourseReportService>()
            .GetAsync(data.Teacher.Id, data.Course.Id, new())).Value!;
        Assert.Equal(1, report.Commerce.PaidEnrollments);
        Assert.Equal(1, report.Commerce.SuccessfulOrders);
        Assert.Equal(data.Course.Price, report.Commerce.SuccessfulPaymentAmount);
    }

    [Fact]
    public async Task ConcurrentFreeEnrollmentHttpPosts_IgnoreSpoofedIdentityAndCreateOneEnrollmentWithoutCommerceRows()
    {
        var data = await SeedAsync(paid: false);
        var otherStudent = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var student = await LoginAsync(data.Student);
        var token = await IdentityTestHelpers.GetTokenAsync(student, $"/Courses/Details/{data.Course.Id}");
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => student.PostAsync(
            $"/Courses/EnrollFree/{data.Course.Id}", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token, ["StudentUserId"] = otherStudent.Id.ToString()
            }))));
        foreach (var response in responses)
        {
            using (response) Assert.Equal($"/Learning/Course/{data.Course.Id}", response.Headers.Location?.ToString());
        }
        await AssertRowsAsync(data, 0, 0, 1);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Enrollments.AnyAsync(e => e.CourseId == data.Course.Id && e.StudentUserId == otherStudent.Id));
        Assert.Null(await db.Enrollments.Where(e => e.CourseId == data.Course.Id).Select(e => e.PaymentId).SingleAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnrolledCourse_UnpublishThenArchiveKeepsReadingButBlocksNewEnrollmentAndArchivedProgress(bool paid)
    {
        var data = await SeedAsync(paid);
        var newcomer = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var student = await LoginAsync(data.Student);
        using var teacher = await LoginAsync(data.Teacher);
        using var other = await LoginAsync(newcomer);
        await EnrollAsync(student, data);
        await CompleteAsync(student, data.First.Id, true);
        await AssertProgressAsync(data, 1, 2, null);

        using var unpublish = await PostAsync(teacher, $"/TeacherCourses/Unpublish/{data.Course.Id}", new());
        Assert.Equal(HttpStatusCode.Redirect, unpublish.StatusCode);
        using var publicDetail = await other.GetAsync($"/Courses/Details/{data.Course.Id}");
        Assert.Equal(HttpStatusCode.NotFound, publicDetail.StatusCode);
        using var newEnrollment = await PostAsync(other,
            paid ? "/Checkout/CreateOrder" : $"/Courses/EnrollFree/{data.Course.Id}", new() { ["CourseId"] = data.Course.Id.ToString() });
        Assert.Equal(paid ? HttpStatusCode.Redirect : HttpStatusCode.NotFound, newEnrollment.StatusCode);
        AssertDenied(await other.GetAsync($"/Learning/Lesson/{data.First.Id}"));
        await CompleteAsync(student, data.Second.Id, true);
        var completedAt = (await ProgressAsync(data)).CompletedAtUtc;
        Assert.NotNull(completedAt);

        using var archive = await PostAsync(teacher, $"/TeacherCourses/Archive/{data.Course.Id}", new());
        Assert.Equal(HttpStatusCode.Redirect, archive.StatusCode);
        using var historical = await student.GetAsync($"/Learning/Lesson/{data.First.Id}");
        Assert.Equal(HttpStatusCode.OK, historical.StatusCode);
        Assert.Contains("WEEK6_PRIVATE_CONTENT", await historical.Content.ReadAsStringAsync());
        Assert.DoesNotContain("action=\"/Learning/Completion/", await historical.Content.ReadAsStringAsync());
        using var forgedProgress = await PostAsync(student, $"/Learning/Completion/{data.First.Id}", new() { ["completed"] = "false" });
        AssertDenied(forgedProgress);
        await AssertProgressAsync(data, 2, 2, completedAt);
        await AssertRowsAsync(data, paid ? 1 : 0, paid ? 1 : 0, 1);
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Enrollments
            .AnyAsync(e => e.CourseId == data.Course.Id && e.StudentUserId == newcomer.Id));
    }

    [Theory]
    [InlineData(CourseStatus.Unpublished)]
    [InlineData(CourseStatus.Archived)]
    public async Task SuccessCallbackAfterCourseWithdrawal_RecordsReceiptAndReplayButDoesNotGrantLearning(CourseStatus status)
    {
        var data = await SeedAsync(paid: true);
        using var student = await LoginAsync(data.Student);
        using var teacher = await LoginAsync(data.Teacher);
        using var callbackClient = IdentityTestHelpers.CreateClient(factory);
        var order = await CreateOrderAsync(student, data.Course.Id);
        var callback = await CallbackAsync(data.Student.Id, order);
        using var withdrawn = await PostAsync(teacher, $"/TeacherCourses/{(status == CourseStatus.Unpublished ? "Unpublish" : "Archive")}/{data.Course.Id}", new());
        Assert.Equal(HttpStatusCode.Redirect, withdrawn.StatusCode);
        for (var i = 0; i < 3; i++)
        {
            using var received = await callbackClient.PostAsJsonAsync("/payments/webhook", callback);
            Assert.Equal(HttpStatusCode.OK, received.StatusCode);
        }
        await AssertRowsAsync(data, 1, 1, 0);
        AssertDenied(await student.GetAsync($"/Learning/Lesson/{data.First.Id}"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(PaymentStatus.Succeeded, await db.Payments.Where(p => p.OrderId == order).Select(p => p.Status).SingleAsync());
        Assert.Empty(await db.LessonProgressEntries.Where(p => p.Lesson.Module.CourseId == data.Course.Id).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenLessonAndModule_ChangeEffectiveProgressAndContinueWithoutErasingHistoricalCompletion(bool paid)
    {
        var data = await SeedAsync(paid);
        using var student = await LoginAsync(data.Student);
        using var teacher = await LoginAsync(data.Teacher);
        await EnrollAsync(student, data);
        await CompleteAsync(student, data.First.Id, true);
        await AssertProgressAsync(data, 1, 2, null);

        await EditContentAsync(teacher, data, ContentKind.Lesson, data.Module.Id, data.First.Id,
            new() { ["Input.Title"] = data.First.Title, ["Input.Status"] = "3" });
        AssertDenied(await student.GetAsync($"/Learning/Lesson/{data.First.Id}"));
        AssertDenied(await student.GetAsync($"/Learning/Resource/{data.First.Resources.Single().Id}"));
        AssertDenied(await PostAsync(student, $"/Learning/Completion/{data.First.Id}", new() { ["completed"] = "false" }));
        using var continued = await student.GetAsync($"/Learning/Continue/{data.Course.Id}");
        Assert.Equal($"/Learning/Lesson/{data.Second.Id}", continued.Headers.Location?.ToString());
        await AssertProgressAsync(data, 0, 1, null);
        using var preview = await teacher.GetAsync($"/Learning/Lesson/{data.First.Id}");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);

        await EditContentAsync(teacher, data, ContentKind.Lesson, data.Module.Id, data.First.Id,
            new() { ["Input.Title"] = data.First.Title, ["Input.Status"] = "2" });
        await AssertProgressAsync(data, 1, 2, null); // The hidden lesson's progress was retained.
        await CompleteAsync(student, data.Second.Id, true);
        var completedAt = (await ProgressAsync(data)).CompletedAtUtc;
        Assert.NotNull(completedAt);
        await EditContentAsync(teacher, data, ContentKind.Lesson, data.Module.Id, data.Draft.Id,
            new() { ["Input.Title"] = data.Draft.Title, ["Input.Status"] = "2" });
        await AssertProgressAsync(data, 2, 3, completedAt);
        await CompleteAsync(student, data.First.Id, false);
        await AssertProgressAsync(data, 1, 3, completedAt);

        await EditContentAsync(teacher, data, ContentKind.Module, data.Course.Id, data.Module.Id,
            new() { ["Input.Title"] = data.Module.Title, ["Input.Visibility"] = "2" });
        var empty = await ProgressAsync(data);
        Assert.Null(empty.Percentage);
        Assert.Null(empty.ContinueLessonId);
        await AssertProgressAsync(data, 0, 0, completedAt);
        using var emptyCourse = await student.GetAsync($"/Learning/Course/{data.Course.Id}");
        Assert.Contains("Chưa có nội dung khả dụng", WebUtility.HtmlDecode(await emptyCourse.Content.ReadAsStringAsync()));
        using var emptyContinue = await student.GetAsync($"/Learning/Continue/{data.Course.Id}");
        Assert.Equal($"/Learning/Course/{data.Course.Id}", emptyContinue.Headers.Location?.ToString());
        await EditContentAsync(teacher, data, ContentKind.Module, data.Course.Id, data.Module.Id,
            new() { ["Input.Title"] = data.Module.Title, ["Input.Visibility"] = "1" });
        await AssertProgressAsync(data, 1, 3, completedAt);
    }

    [Theory]
    [InlineData("Edit")]
    [InlineData("Content")]
    [InlineData("Reorder")]
    [InlineData("DeleteResource")]
    [InlineData("Publish")]
    [InlineData("Unpublish")]
    [InlineData("Archive")]
    public async Task ForeignTeacherMutationWithValidCsrf_CannotAlterCourseOrCommerceOrStudentProgress(string action)
    {
        var data = await SeedAsync(paid: true);
        var foreignTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var student = await LoginAsync(data.Student);
        using var other = await LoginAsync(foreignTeacher);
        await EnrollAsync(student, data);
        await CompleteAsync(student, data.First.Id, true);
        AssertDenied(await other.GetAsync($"/TeacherCourses/Manage/{data.Course.Id}"));
        AssertDenied(await other.GetAsync($"/TeacherCourses/Students/{data.Course.Id}"));
        AssertDenied(await other.GetAsync($"/Learning/Resource/{data.First.Resources.Single().Id}"));
        var path = action switch
        {
            "Content" => $"/TeacherCourses/Content?courseId={data.Course.Id}&kind=Lesson&parentId={data.Module.Id}&id={data.First.Id}",
            "Reorder" => $"/TeacherCourses/Reorder?courseId={data.Course.Id}&kind=Lesson&parentId={data.Module.Id}",
            "DeleteResource" => $"/TeacherCourses/DeleteResource?courseId={data.Course.Id}&lessonId={data.First.Id}&id={data.First.Resources.Single().Id}",
            _ => $"/TeacherCourses/{action}/{data.Course.Id}"
        };
        using var response = await PostAsync(other, path, new()
        {
            ["Input.Title"] = "Hijacked", ["Input.Price"] = "0", ["Input.Level"] = "1",
            ["Input.Status"] = "3", ["OwnerTeacherUserId"] = foreignTeacher.Id.ToString(),
            ["ids[0]"] = data.Second.Id.ToString(), ["ids[1]"] = data.First.Id.ToString(), ["ids[2]"] = data.Draft.Id.ToString()
        });
        AssertDenied(response);
        await AssertRowsAsync(data, 1, 1, 1);
        await AssertProgressAsync(data, 1, 2, null);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = await db.Courses.AsNoTracking().SingleAsync(c => c.Id == data.Course.Id);
        Assert.Equal(data.Course.Title, course.Title);
        Assert.Equal(data.Teacher.Id, course.OwnerTeacherUserId);
        Assert.Equal(CourseStatus.Published, course.Status);
        Assert.Equal(data.Course.Price, course.Price);
        var first = await db.Lessons.AsNoTracking().SingleAsync(l => l.Id == data.First.Id);
        Assert.Equal(data.First.Title, first.Title);
        Assert.Equal(LessonStatus.Published, first.Status);
        Assert.Equal(0, first.OrderIndex);
        Assert.True(await db.LessonResources.AnyAsync(r => r.Id == data.First.Resources.Single().Id));
    }

    private async Task<HttpClient> LoginAsync(ApplicationUser user)
    {
        var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    private static void AssertDenied(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
        }
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await IdentityTestHelpers.GetTokenAsync(client, "/Profile");
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private static async Task CompleteAsync(HttpClient student, Guid lessonId, bool completed)
    {
        using var response = await PostAsync(student, $"/Learning/Completion/{lessonId}", new() { ["completed"] = completed.ToString().ToLowerInvariant() });
        Assert.Equal($"/Learning/Lesson/{lessonId}", response.Headers.Location?.ToString());
    }

    private static async Task EditContentAsync(HttpClient teacher, Data data, ContentKind kind, Guid parent, Guid id, Dictionary<string, string> fields)
    {
        using var response = await PostAsync(teacher,
            $"/TeacherCourses/Content?courseId={data.Course.Id}&kind={kind}&parentId={parent}&id={id}", fields);
        Assert.Equal($"/TeacherCourses/Manage/{data.Course.Id}", response.Headers.Location?.ToString());
    }

    private static async Task<Guid> CreateOrderAsync(HttpClient student, Guid courseId, Guid? spoofedBuyer = null)
    {
        using var response = await PostAsync(student, "/Checkout/CreateOrder", new()
        { ["CourseId"] = courseId.ToString(), ["Amount"] = "1", ["StudentUserId"] = spoofedBuyer?.ToString() ?? Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Checkout/Order/", response.Headers.Location?.ToString());
        return Guid.Parse(response.Headers.Location!.ToString().Split('/').Last());
    }

    private async Task<SignedPaymentCallback> CallbackAsync(Guid studentId, Guid orderId)
    {
        using var scope = factory.Services.CreateScope();
        var order = await scope.ServiceProvider.GetRequiredService<ICheckoutService>().GetOrderAsync(studentId, orderId);
        Assert.Equal(CheckoutCode.Allowed, order.Code);
        return scope.ServiceProvider.GetRequiredService<LocalSandboxGateway>().CreateCallback(order.Value!, SandboxScenario.Success);
    }

    private async Task EnrollAsync(HttpClient student, Data data)
    {
        if (data.Course.IsPaid)
        {
            var order = await CreateOrderAsync(student, data.Course.Id);
            using var response = await PostAsync(student, $"/Checkout/Simulate/{order}", new() { ["scenario"] = "success" });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        else
        {
            using var response = await PostAsync(student, $"/Courses/EnrollFree/{data.Course.Id}", new());
            Assert.Equal($"/Learning/Course/{data.Course.Id}", response.Headers.Location?.ToString());
        }
    }

    private async Task<CourseProgress> ProgressAsync(Data data)
    {
        using var scope = factory.Services.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<ICourseService>().OpenCourseAsync(data.Student.Id, data.Course.Id, false);
        Assert.Equal(LearningAccessResult.Allowed, page.Access);
        return page.Value!.Progress!;
    }

    private async Task AssertProgressAsync(Data data, int completed, int total, DateTimeOffset? historicalCompletion)
    {
        var progress = await ProgressAsync(data);
        Assert.Equal(completed, progress.CompletedLessons);
        Assert.Equal(total, progress.TotalLessons);
        Assert.Equal(historicalCompletion, progress.CompletedAtUtc);
        using var scope = factory.Services.CreateScope();
        var courses = scope.ServiceProvider.GetRequiredService<ICourseService>();
        var mine = (await courses.MyCoursesAsync(data.Student.Id, false)).Value!.Single(c => c.Id == data.Course.Id);
        Assert.Equal(progress, mine.Progress);
        var report = (await scope.ServiceProvider.GetRequiredService<ITeacherCourseReportService>()
            .GetAsync(data.Teacher.Id, data.Course.Id, new())).Value!;
        var student = Assert.Single(report.Students);
        Assert.Equal(completed, student.Progress.CompletedLessons);
        Assert.Equal(total, student.Progress.TotalLessons);
        Assert.Equal(historicalCompletion, student.Progress.CompletedAtUtc);
    }

    private async Task AssertRowsAsync(Data data, int orders, int payments, int enrollments)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(orders, await db.Orders.CountAsync(o => o.CourseId == data.Course.Id));
        Assert.Equal(payments, await db.Payments.CountAsync(p => p.Order.CourseId == data.Course.Id));
        Assert.Equal(enrollments, await db.Enrollments.CountAsync(e => e.CourseId == data.Course.Id && e.StudentUserId == data.Student.Id));
        Assert.All(await db.Orders.Where(o => o.CourseId == data.Course.Id).ToListAsync(), order =>
        {
            Assert.Equal(data.Student.Id, order.StudentUserId);
            Assert.Equal(data.Course.Price, order.Amount);
            Assert.Equal("VND", order.Currency);
        });
    }

    private async Task<Data> SeedAsync(bool paid)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var first = Lesson("First", 0);
        var second = Lesson("Second", 1);
        var draft = Lesson("Draft", 2); draft.Status = LessonStatus.Draft;
        var module = new Module { Title = "Week 6 visible module", Lessons = [first, second, draft] };
        var course = new Course
        {
            OwnerTeacherUserId = teacher.Id, Title = "Week 6 journey " + Guid.NewGuid().ToString("N"),
            Status = CourseStatus.Published, IsPaid = paid, Price = paid ? 199000m : 0m,
            Modules = [module, new Module { Title = "Hidden module", OrderIndex = 1, Visibility = ModuleVisibility.Hidden,
                Lessons = [Lesson("Hidden parent lesson", 0)] }]
        };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        return new(teacher, student, course, module, first, second, draft);
    }

    private static Lesson Lesson(string title, int index) => new()
    {
        Title = title, OrderIndex = index, Status = LessonStatus.Published,
        Resources = [new LessonResource { ResourceType = LessonResourceType.Text, ContentText = "WEEK6_PRIVATE_CONTENT " + title }]
    };

    private sealed record Data(ApplicationUser Teacher, ApplicationUser Student, Course Course, Module Module, Lesson First, Lesson Second, Lesson Draft);
}
