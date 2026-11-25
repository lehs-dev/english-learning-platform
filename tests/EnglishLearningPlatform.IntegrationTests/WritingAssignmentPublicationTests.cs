using System.Net;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class WritingAssignmentPublicationTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task HomeworkDraftAndScheduleStayPrivateUntilPublication_ThenLockEditing()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero));
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        var owner = await IdentityTestHelpers.CreateUserAsync(host, AppRoles.Teacher);
        var outsideTeacher = await IdentityTestHelpers.CreateUserAsync(host, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(host, AppRoles.Student);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new Course { OwnerTeacherUserId = owner.Id, Title = "Lớp luyện viết", Status = CourseStatus.Published };
        db.Enrollments.Add(new Enrollment { Course = course, StudentUserId = student.Id });
        await db.SaveChangesAsync();

        using var teacher = await LoginAsync(host, owner);
        using var outsider = await LoginAsync(host, outsideTeacher);
        using var learner = await LoginAsync(host, student);
        var create = $"/Homework/Create/{course.Id}";
        var courseHomework = $"/Homework/Course/{course.Id}";
        var createToken = await IdentityTestHelpers.GetTokenAsync(teacher, create);
        const string originalTitle = "Bài viết đang soạn";
        const string scheduledTitle = "Bài viết mở theo lịch";
        const string prompt = "Describe your plans for tomorrow.";
        using var created = await PostAsync(teacher, create, createToken,
            ("Title", originalTitle), ("Prompt", prompt), ("PublishMode", "Draft"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var draft = await db.WritingAssignments.AsNoTracking().SingleAsync(row => row.CourseId == course.Id);
        var details = $"/Homework/Details/{draft.Id}";
        var edit = $"/Homework/Edit/{draft.Id}";
        Assert.Equal(details, created.Headers.Location!.OriginalString);
        Assert.Contains(prompt, await teacher.GetStringAsync(details));
        Assert.Contains(originalTitle, WebUtility.HtmlDecode(await teacher.GetStringAsync(courseHomework)));
        using var outsiderEdit = await outsider.GetAsync(edit);
        Assert.True(outsiderEdit.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Redirect);
        if (outsiderEdit.StatusCode == HttpStatusCode.Redirect)
            Assert.Contains("/Account/AccessDenied", outsiderEdit.Headers.Location!.OriginalString);
        await AssertHiddenAsync(originalTitle);

        var learnerToken = await IdentityTestHelpers.GetTokenAsync(learner, "/Profile");
        foreach (string action in new[] { "Submit", "SaveDraft" })
        {
            using var blocked = await PostAsync(learner, $"/Homework/{action}/{draft.Id}", learnerToken,
                ("response", "This must remain invisible."));
            Assert.Equal(HttpStatusCode.NotFound, blocked.StatusCode);
        }
        Assert.False(await db.WritingSubmissions.AnyAsync(row => row.AssignmentId == draft.Id));
        Assert.False(await db.WritingSubmissionDrafts.AnyAsync(row => row.AssignmentId == draft.Id));

        var editToken = await IdentityTestHelpers.GetTokenAsync(teacher, edit);
        const string releaseLocal = "2026-11-24T19:00";
        const string dueLocal = "2026-11-24T20:00";
        using var invalid = await PostAsync(teacher, edit, editToken, ("Title", scheduledTitle), ("Prompt", prompt),
            ("PublishMode", "Scheduled"), ("PublishAt", releaseLocal), ("DueAt", releaseLocal));
        Assert.NotEqual(HttpStatusCode.Redirect, invalid.StatusCode);
        Assert.Equal(originalTitle, await db.WritingAssignments.Where(row => row.Id == draft.Id).Select(row => row.Title).SingleAsync());

        using var scheduled = await PostAsync(teacher, edit, editToken, ("Title", scheduledTitle), ("Prompt", prompt),
            ("PublishMode", "Scheduled"), ("PublishAt", releaseLocal), ("DueAt", dueLocal));
        Assert.Equal(HttpStatusCode.Redirect, scheduled.StatusCode);
        Assert.Equal(details, scheduled.Headers.Location!.OriginalString);
        Assert.Contains(scheduledTitle, WebUtility.HtmlDecode(await teacher.GetStringAsync(details)));
        Assert.Equal(new DateTimeOffset(2026, 11, 24, 13, 0, 0, TimeSpan.Zero),
            await db.WritingAssignments.Where(row => row.Id == draft.Id).Select(row => row.DueAtUtc).SingleAsync());

        var release = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
        clock.UtcNow = release.AddSeconds(-1);
        await AssertHiddenAsync(scheduledTitle);
        clock.UtcNow = release;
        Assert.Contains(prompt, await learner.GetStringAsync(details));
        Assert.Contains(scheduledTitle, WebUtility.HtmlDecode(await learner.GetStringAsync(courseHomework)));
        Assert.Contains(details, await learner.GetStringAsync("/Learning/Dashboard"));
        using var releasedEdit = await teacher.GetAsync(edit);
        Assert.Equal(HttpStatusCode.Redirect, releasedEdit.StatusCode);
        Assert.Equal(details, releasedEdit.Headers.Location!.OriginalString);

        using var staleEdit = await PostAsync(teacher, edit, editToken, ("Title", "Stale tab"),
            ("Prompt", "This must not become the new prompt."), ("PublishMode", "Now"));
        Assert.Equal(HttpStatusCode.Redirect, staleEdit.StatusCode);
        Assert.Equal(details, staleEdit.Headers.Location!.OriginalString);
        Assert.DoesNotContain("This must not become the new prompt.", await learner.GetStringAsync(details));

        const string immediateTitle = "Bài viết giao ngay";
        using var anotherDraft = await PostAsync(teacher, create, createToken,
            ("Title", immediateTitle), ("Prompt", prompt), ("PublishMode", "Draft"));
        Assert.Equal(HttpStatusCode.Redirect, anotherDraft.StatusCode);
        var immediate = await db.WritingAssignments.Where(row => row.CourseId == course.Id && row.Title == immediateTitle)
            .Select(row => row.Id).SingleAsync();
        var immediateDetails = $"/Homework/Details/{immediate}";
        using var unpublished = await learner.GetAsync(immediateDetails);
        Assert.Equal(HttpStatusCode.NotFound, unpublished.StatusCode);
        var immediateEdit = $"/Homework/Edit/{immediate}";
        var immediateToken = await IdentityTestHelpers.GetTokenAsync(teacher, immediateEdit);
        using var published = await PostAsync(teacher, immediateEdit, immediateToken,
            ("Title", immediateTitle), ("Prompt", prompt), ("PublishMode", "Now"));
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        Assert.Equal(immediateDetails, published.Headers.Location!.OriginalString);
        Assert.Contains(prompt, await learner.GetStringAsync(immediateDetails));

        async Task AssertHiddenAsync(string title)
        {
            using var direct = await learner.GetAsync(details);
            Assert.Equal(HttpStatusCode.NotFound, direct.StatusCode);
            Assert.DoesNotContain(title, WebUtility.HtmlDecode(await learner.GetStringAsync(courseHomework)));
            Assert.DoesNotContain(details, await learner.GetStringAsync("/Learning/Dashboard"));
        }
    }

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> host, ApplicationUser user)
    {
        var client = IdentityTestHelpers.CreateClient(host);
        using var response = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string token,
        params (string Name, string Value)[] fields)
    {
        using var form = new FormUrlEncodedContent(fields.Select(field => KeyValuePair.Create(field.Name, field.Value))
            .Append(KeyValuePair.Create("__RequestVerificationToken", token)));
        return await client.PostAsync(path, form);
    }

    private sealed class MutableClock(DateTimeOffset start) : TimeProvider
    {
        private long utcTicks = start.UtcTicks;

        public DateTimeOffset UtcNow { set => Interlocked.Exchange(ref utcTicks, value.UtcTicks); }
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref utcTicks), TimeSpan.Zero);
    }
}
