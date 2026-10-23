using System.Net;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class TeacherCourseListTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task List_CombinesVietnameseSearchFiltersAndPagination_WithinOwnCoursesOnly()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        await Seed(teacher.Id, 7);
        await Seed(other.Id, 1, "Khóa học nghe của người khác");
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, teacher);
        using var response = await client.GetAsync("/TeacherCourses?Search=khoa%20hoc%20nghe&Status=1&Level=2&Skill=4&Sort=title&Page=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var card = Assert.Single(Cards(html));
        Assert.Contains("Khóa học nghe 07", card);
        Assert.DoesNotContain("của người khác", html);
        Assert.Contains("Hiển thị 7–7 trong 7 khóa học", html);
        Assert.Contains("Sơ cấp", card);
        Assert.Contains("Nghe hiểu", card);
        Assert.Contains("aria-label=\"Trang trước\"", html);
        foreach (var part in new[] { "Search=khoa%20hoc%20nghe", "Status=1", "Level=2", "Skill=4", "Sort=title", "Page=1" })
            Assert.Contains(part, html);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(7, await db.Courses.CountAsync(c => c.OwnerTeacherUserId == teacher.Id));
        Assert.False(await db.Practices.AnyAsync(p => p.OwnerTeacherUserId == teacher.Id));
    }

    [Fact]
    public async Task List_NormalizesInvalidQuery_AndOrdersPagesByExistingCreationOrder()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        await Seed(teacher.Id, 8);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, teacher);
        using var newest = await client.GetAsync("/TeacherCourses");
        Assert.Contains("Khóa học nghe 08", Cards(WebUtility.HtmlDecode(await newest.Content.ReadAsStringAsync()))[0]);
        using var oldest = await client.GetAsync("/TeacherCourses?Sort=oldest");
        Assert.Contains("Khóa học nghe 01", Cards(WebUtility.HtmlDecode(await oldest.Content.ReadAsStringAsync()))[0]);
        using var invalid = await client.GetAsync("/TeacherCourses?Status=abc&Level=999&Skill=-1&Sort=unknown&Page=2147483647");
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        var html = WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync());
        Assert.Equal(2, Cards(html).Count);
        Assert.Contains("Hiển thị 7–8 trong 8 khóa học", html);
        Assert.DoesNotContain("field-validation-error", html);
        using var empty = await client.GetAsync("/TeacherCourses?Search=khong%20co%20ket%20qua");
        var emptyHtml = WebUtility.HtmlDecode(await empty.Content.ReadAsStringAsync());
        Assert.Empty(Cards(emptyHtml));
        Assert.Contains("Không tìm thấy khóa học phù hợp", emptyHtml);
        Assert.Contains("Hiển thị 0–0 trong 0 khóa học", emptyHtml);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await db.Courses.FirstAsync(c => c.OwnerTeacherUserId == teacher.Id);
            course.Title = "Luyện giao tiếp ở trường học";
            await db.SaveChangesAsync();
        }
        using var accentSearch = await client.GetAsync("/TeacherCourses?Search=luyen%20giao%20tiep%20o%20truong");
        Assert.Contains("Luyện giao tiếp ở trường học", Assert.Single(Cards(WebUtility.HtmlDecode(await accentSearch.Content.ReadAsStringAsync()))));
    }

    [Fact]
    public async Task List_IncludesEachLifecycleStatus_AndKeepsAllManagementLinks()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var courses = Enum.GetValues<CourseStatus>().Select(status => new Course
        {
            OwnerTeacherUserId = teacher.Id, Title = "Khóa học " + (int)status, Status = status
        }).ToArray();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Courses.AddRange(courses);
            await db.SaveChangesAsync();
        }
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, teacher);
        foreach (var course in courses)
        {
            using var response = await client.GetAsync("/TeacherCourses?Status=" + (int)course.Status);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var card = Assert.Single(Cards(WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync())));
            Assert.Contains(course.Title, card);
            Assert.Contains($"/TeacherCourses/Manage/{course.Id}", card);
            Assert.Contains($"/TeacherCourses/Edit/{course.Id}", card);
            Assert.Contains($"/TeacherCourses/Students/{course.Id}", card);
            Assert.Contains($"/Learning/Course/{course.Id}", card);
            Assert.Contains("Chưa chọn kỹ năng", card);
        }
    }

    private async Task Seed(Guid owner, int count, string? title = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Courses.AddRange(Enumerable.Range(1, count).Select(index => new Course
        {
            OwnerTeacherUserId = owner, Title = title ?? $"Khóa học nghe {index:00}",
            Level = CourseLevel.Elementary, Status = CourseStatus.Draft,
            CreatedAtUtc = DateTimeOffset.UtcNow.Date.AddDays(index - 30),
            Skills = [new CourseSkill { Skill = EnglishSkill.Listening }]
        }));
        await db.SaveChangesAsync();
    }

    private static List<string> Cards(string html) => Regex.Matches(html, "<article\\b[^>]*class=\"[^\"]*teacher-course-card[^\"]*\"[^>]*>.*?</article>", RegexOptions.Singleline)
        .Select(match => match.Value).ToList();
}
