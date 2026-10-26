using System.Net;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Gửi HTTP qua routing, cookie, authorization và Razor thật.
// Fixture tạo SQL Server test riêng, không đọc/ghi database demo của người dùng.
public sealed class TeacherPracticeIndexWebTests : IClassFixture<IntegrationTestFactory>
{
    private const string IndexPath = "/TeacherPractices";
    private static readonly DateTimeOffset CreatedAt =
        new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private readonly IntegrationTestFactory _factory;

    public TeacherPracticeIndexWebTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Guest_ListRequiresLoginAndPreservesReturnUrl()
    {
        using var client = IdentityTestHelpers.CreateClient(_factory);

        using var response = await client.GetAsync(IndexPath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal("/Account/Login", Url(response.Headers.Location.ToString()).AbsolutePath);
        Assert.Equal(IndexPath, Query(response.Headers.Location.ToString())["ReturnUrl"].ToString());
    }

    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Admin)]
    public async Task NonTeacher_CannotReadTeacherList(string role)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(_factory, role);
        using var client = await LoginAsync(user);

        using var response = await client.GetAsync(IndexPath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", Url(response.Headers.Location!.ToString()).AbsolutePath);
    }

    [Fact]
    public async Task TeacherWithoutCoursesOrPractices_GetsPrivateEmptyTeacherPage()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var client = await LoginAsync(teacher);
        using var response = await client.GetAsync(IndexPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl.NoStore);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Bài luyện tập của tôi", WebUtility.HtmlDecode(html));
        Assert.Contains("teacher-studio", html);
        Assert.Contains("studio-nav", html);
        Assert.Contains("/css/courses.css", html);
        Assert.Contains("/css/dashboard.css", html);
        Assert.Contains("/css/role-layouts.css", html);
        Assert.Contains("practice-empty", html);
        Assert.Empty(Rows(html));
        Assert.Single(OptionTags(html, "CourseId")); // Chỉ có lựa chọn tất cả Course.
        Assert.Empty(PaginationBlocks(html));
        Assert.DoesNotContain("Trang 1 / 0", WebUtility.HtmlDecode(html));
        Assert.Contains("/TeacherPractices/Create", Attribute(LinkByText(html, "+ Tạo bài luyện tập"), "href"));
    }

    [Theory]
    [InlineData("/TeacherCourses", false)]
    [InlineData("/TeacherQuestionBank", false)]
    [InlineData("/TeacherPractices", true)]
    [InlineData("/TeacherPractices?Search=grammar&Status=Draft&PageNumber=3", true)]
    [InlineData("/TeacherPractices/Create", true)]
    public async Task PracticeSidebarLink_OpensListAndIsActiveOnlyInPracticeSection(
        string path, bool shouldBeActive)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var client = await LoginAsync(teacher);
        var before = await CountsAsync();

        var html = await HtmlAsync(client, path);

        // Chỉ kiểm tra menu sidebar, không nhầm với nút trong nội dung trang.
        var sidebar = Assert.Single(
            Regex.Matches(html, "<nav\\b[^>]*>.*?</nav>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline)
                .Select(match => match.Value),
            nav => Attribute(nav, "class").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("studio-nav"));
        var practiceLink = LinkByText(sidebar, "Bài luyện tập");
        var href = Attribute(practiceLink, "href");
        var classes = Attribute(practiceLink, "class").Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(IndexPath, href);
        Assert.Empty(Url(href).Fragment); // Không quay lại anchor của trang Khóa học cũ.
        Assert.Empty(Url(href).Query); // Menu mở danh sách mặc định, không giữ bộ lọc trang hiện tại.
        Assert.Contains("studio-nav-link", classes);
        Assert.Contains("/images/icons.svg#check-circle", practiceLink);
        Assert.Equal(shouldBeActive, classes.Contains("is-active"));
        Assert.Equal(shouldBeActive ? "page" : string.Empty, Attribute(practiceLink, "aria-current"));

        // Mỗi trang chỉ có một mục được chọn; mục Khóa học/Question Bank vẫn hoạt động đúng.
        var activeLink = Assert.Single(Links(sidebar), link =>
            Attribute(link, "class").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("is-active"));
        if (shouldBeActive)
        {
            Assert.Equal(practiceLink, activeLink);
        }
        else
        {
            Assert.Equal(Url(path).AbsolutePath, Url(Attribute(activeLink, "href")).AbsolutePath);
        }

        // Đi theo URL thật do Razor sinh ra, không chỉ kiểm tra chuỗi HTML.
        var listHtml = await HtmlAsync(client, href);
        Assert.Contains("Bài luyện tập của tôi", WebUtility.HtmlDecode(listHtml));
        Assert.Empty(Rows(listHtml));
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task List_ShowsOwnedStatesParentMetadataCountsAndUtcButNoForeignOrQuestionDetails()
    {
        var data = await SeedAsync();
        await AddPracticeAsync(data.Teacher.Id, "OWN_UNATTACHED", questionCount: 1);
        await AddPracticeAsync(data.Teacher.Id, "OWN_LESSON", lessonId: data.Own.LessonId,
            status: PracticeStatus.Published, questionCount: 2, createdAt: CreatedAt.AddMinutes(1));
        await AddPracticeAsync(data.Teacher.Id, "OWN_MODULE", moduleId: data.Own.ModuleId,
            status: PracticeStatus.Unpublished, questionCount: 3, createdAt: CreatedAt.AddMinutes(2));
        var foreignId = await AddPracticeAsync(data.OtherTeacher.Id, "FOREIGN_PRACTICE_PRIVATE",
            lessonId: data.Other.LessonId, questionCount: 1);
        using var client = await LoginAsync(data.Teacher);

        var html = await HtmlAsync(client, IndexPath);
        var rows = Rows(html);

        Assert.Equal(new[] { "OWN_MODULE", "OWN_LESSON", "OWN_UNATTACHED" }, rows.Select(RowTitle));
        var moduleCells = Cells(rows[0]);
        Assert.Contains("Ngừng xuất bản", Text(moduleCells[1]));
        Assert.Equal(data.Own.CourseTitle, Text(Assert.Single(Links(moduleCells[2]))));
        Assert.Contains("Chương học: " + data.Own.ModuleTitle, Text(moduleCells[2]));
        Assert.Equal("3", Text(moduleCells[3]));
        Assert.Contains("15/10/2026 08:02 UTC", Text(moduleCells[0]));
        var lessonCells = Cells(rows[1]);
        Assert.Contains("Bài học: " + data.Own.LessonTitle, Text(lessonCells[2]));
        Assert.DoesNotContain(data.Own.ModuleTitle, Text(lessonCells[2]));
        Assert.Equal("2", Text(lessonCells[3]));
        Assert.Contains("Chưa gắn khóa học", Text(Cells(rows[2])[2]));
        Assert.Contains("Chưa chọn vị trí", Text(Cells(rows[2])[2]));
        Assert.Contains("Ngày tạo hiển thị theo UTC", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("FOREIGN_PRACTICE_PRIVATE", html);
        Assert.DoesNotContain(foreignId.ToString(), html);
        Assert.DoesNotContain(data.Other.CourseTitle, html);
        Assert.DoesNotContain(data.Other.CourseId.ToString(), html);
        Assert.DoesNotContain("QUESTION_PRIVATE_CONTENT", html);
        Assert.DoesNotContain("QUESTION_PRIVATE_CORRECT_OPTION", html);
        Assert.DoesNotContain("QUESTION_PRIVATE_EXPLANATION", html);
    }

    [Fact]
    public async Task FilterForm_UsesGetFlatNamesAndResetsPageWithoutPostingOwnerOrDropdownData()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var client = await LoginAsync(teacher);
        var html = await HtmlAsync(client, IndexPath + "?PageNumber=9&PageSize=7");

        var form = SearchForm(html);
        var names = Regex.Matches(form, "<(?:input|select)\\b[^>]*>", RegexOptions.IgnoreCase)
            .Select(match => Attribute(match.Value, "name")).OrderBy(name => name).ToArray();

        Assert.Equal(new[] { "CourseId", "PageNumber", "PageSize", "Search", "Status" }, names);
        Assert.Equal("1", Attribute(Input(form, "PageNumber"), "value"));
        Assert.Equal("7", Attribute(Input(form, "PageSize"), "value"));
        Assert.Equal("1", Attribute(Input(form, "PageSize"), "min"));
        Assert.Equal("100", Attribute(Input(form, "PageSize"), "max"));
        Assert.DoesNotContain("Query.", form);
        Assert.DoesNotContain("OwnerTeacherUserId", form);
        Assert.DoesNotContain("__RequestVerificationToken", form); // Token của Logout nằm ngoài form GET này.
    }

    [Theory]
    [InlineData(PracticeStatus.Draft)]
    [InlineData(PracticeStatus.Published)]
    [InlineData(PracticeStatus.Unpublished)]
    public async Task StatusFilter_BindsFromFlatQueryAndKeepsSelection(PracticeStatus status)
    {
        var data = await SeedAsync();
        foreach (PracticeStatus candidate in Enum.GetValues<PracticeStatus>())
        {
            await AddPracticeAsync(data.Teacher.Id, "STATUS_" + candidate,
                moduleId: data.Own.ModuleId, status: candidate, questionCount: 1);
        }
        await AddPracticeAsync(data.OtherTeacher.Id, "FOREIGN_STATUS_MATCH",
            moduleId: data.Other.ModuleId, status: status, questionCount: 1);
        using var client = await LoginAsync(data.Teacher);

        var html = await HtmlAsync(client, IndexPath + "?Status=" + status);

        Assert.Equal("STATUS_" + status, RowTitle(Assert.Single(Rows(html))));
        AssertSelected(html, "Status", status.ToString());
        Assert.DoesNotContain("FOREIGN_STATUS_MATCH", html);
    }

    [Fact]
    public async Task CourseFilter_IncludesBothDirectParentsButNotUnattachedOtherCourseOrOtherOwner()
    {
        var data = await SeedAsync();
        await AddPracticeAsync(data.Teacher.Id, "FILTER_MODULE", moduleId: data.Own.ModuleId);
        await AddPracticeAsync(data.Teacher.Id, "FILTER_LESSON", lessonId: data.Own.LessonId,
            createdAt: CreatedAt.AddMinutes(1));
        await AddPracticeAsync(data.Teacher.Id, "FILTER_UNATTACHED");
        await AddPracticeAsync(data.Teacher.Id, "FILTER_OTHER_OWN_COURSE", moduleId: data.SecondOwn.ModuleId);
        await AddPracticeAsync(data.OtherTeacher.Id, "FILTER_FOREIGN_OWNER", lessonId: data.Own.LessonId);
        using var client = await LoginAsync(data.Teacher);

        var html = await HtmlAsync(client, IndexPath + "?CourseId=" + data.Own.CourseId);

        Assert.Equal(new[] { "FILTER_LESSON", "FILTER_MODULE" }, Rows(html).Select(RowTitle));
        AssertSelected(html, "CourseId", data.Own.CourseId.ToString());
        Assert.DoesNotContain("FILTER_UNATTACHED", html);
        Assert.DoesNotContain("FILTER_OTHER_OWN_COURSE", html);
        Assert.DoesNotContain("FILTER_FOREIGN_OWNER", html);
        Assert.DoesNotContain(data.Other.CourseTitle, html);
        Assert.DoesNotContain(data.Other.CourseId.ToString(), html);
    }

    [Fact]
    public async Task CombinedFiltersAndPaging_KeepInputsDropdownAndAllFiltersInBothNavigationLinks()
    {
        var data = await SeedAsync();
        const string search = "  grammar & vocabulary  ";
        for (int index = 0; index < 3; index++)
        {
            await AddPracticeAsync(data.Teacher.Id, "grammar & vocabulary " + index,
                moduleId: data.Own.ModuleId, createdAt: CreatedAt.AddMinutes(index));
        }
        await AddPracticeAsync(data.Teacher.Id, "grammar & vocabulary wrong Course",
            moduleId: data.SecondOwn.ModuleId);
        await AddPracticeAsync(data.Teacher.Id, "grammar & vocabulary wrong status",
            moduleId: data.Own.ModuleId, status: PracticeStatus.Published, questionCount: 1);
        await AddPracticeAsync(data.Teacher.Id, "Unrelated title", moduleId: data.Own.ModuleId);
        using var client = await LoginAsync(data.Teacher);
        var path = FilterPath(search, PracticeStatus.Draft, data.Own.CourseId, 2, 1);

        var html = await HtmlAsync(client, path);

        Assert.Equal("grammar & vocabulary 1", RowTitle(Assert.Single(Rows(html))));
        Assert.Equal(search, Attribute(Input(SearchForm(html), "Search"), "value"));
        Assert.Equal("1", Attribute(Input(SearchForm(html), "PageNumber"), "value"));
        AssertSelected(html, "Status", "Draft");
        AssertSelected(html, "CourseId", data.Own.CourseId.ToString());
        Assert.Contains("Trang 2 / 3", Text(Pagination(html)));
        var previous = Attribute(LinkByText(Pagination(html), "← Trang trước"), "href");
        var next = Attribute(LinkByText(Pagination(html), "Trang sau →"), "href");
        AssertFilters(previous, search, PracticeStatus.Draft, data.Own.CourseId, 1, 1);
        AssertFilters(next, search, PracticeStatus.Draft, data.Own.CourseId, 3, 1);
        Assert.Equal("grammar & vocabulary 2", RowTitle(Assert.Single(Rows(await HtmlAsync(client, previous)))));
        Assert.Equal("grammar & vocabulary 0", RowTitle(Assert.Single(Rows(await HtmlAsync(client, next)))));
    }

    [Fact]
    public async Task DefaultPagination_HasTenRowsThenLastPageWithoutRepeatingOrLeakingForeignRows()
    {
        var data = await SeedAsync();
        var expected = new List<string>();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (int index = 0; index < 23; index++)
            {
                string title = "PRACTICE_ORDER_" + index;
                expected.Add(title);
                db.Practices.Add(new Practice
                {
                    OwnerTeacherUserId = data.Teacher.Id, Title = title,
                    CreatedAtUtc = CreatedAt.AddMinutes(index)
                });
            }
            await db.SaveChangesAsync();
        }
        expected.Reverse();
        await AddPracticeAsync(data.OtherTeacher.Id, "FOREIGN_NEWEST", createdAt: CreatedAt.AddDays(1));
        using var client = await LoginAsync(data.Teacher);
        var actual = new List<string>();
        string path = IndexPath;
        for (int page = 1; page <= 3; page++)
        {
            var html = await HtmlAsync(client, path);
            var rows = Rows(html);
            Assert.Equal(page == 3 ? 3 : 10, rows.Count);
            Assert.Contains($"Trang {page} / 3", Text(Pagination(html)));
            actual.AddRange(rows.Select(RowTitle));
            Assert.DoesNotContain("FOREIGN_NEWEST", html);
            if (page < 3)
            {
                path = Attribute(LinkByText(Pagination(html), "Trang sau →"), "href");
                Assert.Equal((page + 1).ToString(), Query(path)["PageNumber"].ToString());
            }
            else
            {
                Assert.DoesNotContain(Links(Pagination(html)), link => Text(link) == "Trang sau →");
            }
        }

        Assert.Equal(expected.ToArray(), actual.ToArray());
        Assert.Equal(23, actual.Distinct().Count());
    }

    [Theory]
    [InlineData(99)]
    [InlineData(int.MaxValue)]
    public async Task PageBeyondResults_ShowsRecoveryLinkKeepingFiltersWithoutOverflow(int page)
    {
        var data = await SeedAsync();
        await AddPracticeAsync(data.Teacher.Id, "grammar recovery", lessonId: data.Own.LessonId);
        using var client = await LoginAsync(data.Teacher);

        var html = await HtmlAsync(client,
            FilterPath("grammar", PracticeStatus.Draft, data.Own.CourseId, page, 100));

        Assert.Empty(Rows(html));
        Assert.Contains("Trang bạn yêu cầu không có bài luyện tập", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain($"Trang {page} /", Text(Pagination(html)));
        var first = Attribute(LinkByText(Pagination(html), "Về trang đầu"), "href");
        AssertFilters(first, "grammar", PracticeStatus.Draft, data.Own.CourseId, 1, 100);
        Assert.Equal("grammar recovery", RowTitle(Assert.Single(Rows(await HtmlAsync(client, first)))));
    }

    [Fact]
    public async Task SearchWithNoMatches_ShowsEmptyStateWithoutPageOneOfZero()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        await AddPracticeAsync(teacher.Id, "Existing practice");
        using var client = await LoginAsync(teacher);

        var html = await HtmlAsync(client, IndexPath + "?Search=NO_MATCHING_PRACTICE");

        Assert.Empty(Rows(html));
        Assert.Contains("practice-empty", html);
        Assert.Empty(PaginationBlocks(html));
        Assert.DoesNotContain("Trang 1 / 0", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task ResetFilterLink_RemovesFiltersAndReturnsDefaultList()
    {
        var data = await SeedAsync();
        await AddPracticeAsync(data.Teacher.Id, "Selected grammar", moduleId: data.Own.ModuleId);
        await AddPracticeAsync(data.Teacher.Id, "Other unattached");
        using var client = await LoginAsync(data.Teacher);
        var filtered = await HtmlAsync(client,
            FilterPath("grammar", PracticeStatus.Draft, data.Own.CourseId, 1, 1));

        string reset = Attribute(LinkByText(filtered, "Xóa bộ lọc"), "href");
        Assert.True(IsIndexUrl(reset));
        Assert.Empty(Query(reset));
        var html = await HtmlAsync(client, reset);

        Assert.Equal(2, Rows(html).Count);
        Assert.Equal(string.Empty, Attribute(Input(SearchForm(html), "Search"), "value"));
        Assert.Equal("10", Attribute(Input(SearchForm(html), "PageSize"), "value"));
        Assert.DoesNotContain(OptionTags(html, "CourseId"),
            option => Attribute(option, "value") != string.Empty && IsSelected(option));
        Assert.DoesNotContain(OptionTags(html, "Status"),
            option => Attribute(option, "value") != string.Empty && IsSelected(option));
    }

    [Theory]
    [InlineData("PageNumber=abc")]
    [InlineData("PageNumber=0")]
    [InlineData("PageNumber=-1")]
    [InlineData("PageNumber=2147483648")]
    [InlineData("PageSize=abc")]
    [InlineData("PageSize=0")]
    [InlineData("PageSize=101")]
    [InlineData("PageSize=2147483648")]
    [InlineData("Status=not-a-status")]
    [InlineData("Status=0")]
    [InlineData("Status=999")]
    [InlineData("CourseId=not-a-guid")]
    [InlineData("CourseId=00000000-0000-0000-0000-000000000000")]
    public async Task InvalidQuery_ReturnsBadRequestRatherThanServerError(string query)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        await AddPracticeAsync(teacher.Id, "PRIVATE_MUST_NOT_RENDER_ON_ERROR");
        using var client = await LoginAsync(teacher);

        using var response = await client.GetAsync(IndexPath + "?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("PRIVATE_MUST_NOT_RENDER_ON_ERROR", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedForeignOrUnknownCourse_ReturnsSameNotFoundStatusWithoutMetadata(bool unknown)
    {
        var data = await SeedAsync();
        await AddPracticeAsync(data.OtherTeacher.Id, "PRIVATE_FOREIGN_PRACTICE", moduleId: data.Other.ModuleId);
        using var client = await LoginAsync(data.Teacher);
        Guid courseId = unknown ? Guid.NewGuid() : data.Other.CourseId;

        using var response = await client.GetAsync(IndexPath + "?CourseId=" + courseId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(data.Other.CourseTitle, html);
        Assert.DoesNotContain("PRIVATE_FOREIGN_PRACTICE", html);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task PageSizeAtLimits_IsAcceptedAndRendered(int pageSize)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var client = await LoginAsync(teacher);

        var html = await HtmlAsync(client, IndexPath + "?PageSize=" + pageSize);

        Assert.Equal(pageSize.ToString(), Attribute(Input(SearchForm(html), "PageSize"), "value"));
    }

    [Theory]
    [InlineData(AccountStatus.Locked)]
    [InlineData(AccountStatus.Disabled)]
    public async Task ExistingTeacherCookie_AfterAccountBlocked_CannotReadList(AccountStatus status)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        await AddPracticeAsync(teacher.Id, "PRIVATE_BLOCKED_ACCOUNT_PRACTICE");
        using var client = await LoginAsync(teacher);
        await HtmlAsync(client, IndexPath);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(user => user.Id == teacher.Id);
            user.AccountStatus = status;
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync(IndexPath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", Url(response.Headers.Location!.ToString()).AbsolutePath);
        Assert.DoesNotContain("PRIVATE_BLOCKED_ACCOUNT_PRACTICE", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Admin)]
    public async Task ExistingTeacherCookie_AfterRoleRemovedOrReplaced_CannotReadList(string? replacement)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var client = await LoginAsync(teacher);
        await HtmlAsync(client, IndexPath);
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(teacher.Id.ToString());
            Assert.NotNull(user);
            // Giữ nguyên unique key một role/user; xóa role cũ trước khi thay.
            Assert.True((await users.RemoveFromRoleAsync(user, AppRoles.Teacher)).Succeeded);
            if (replacement != null)
                Assert.True((await users.AddToRoleAsync(user, replacement)).Succeeded);
        }

        using var response = await client.GetAsync(IndexPath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", Url(response.Headers.Location!.ToString()).AbsolutePath);
    }

    [Fact]
    public async Task TitlesParentLabelsAndSearchAttributes_AreHtmlEncoded()
    {
        var data = await SeedAsync();
        const string title = "PRACTICE_<script>alert('practice')</script>";
        const string courseTitle = "COURSE_<script>alert('course')</script>";
        const string lessonTitle = "LESSON_<img src=x onerror=alert('lesson')>";
        const string search = "\" autofocus onfocus=alert('search') data-x=\"";
        await AddPracticeAsync(data.Teacher.Id, title, lessonId: data.Own.LessonId);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Courses.SingleAsync(course => course.Id == data.Own.CourseId)).Title = courseTitle;
            (await db.Lessons.SingleAsync(lesson => lesson.Id == data.Own.LessonId)).Title = lessonTitle;
            await db.SaveChangesAsync();
        }
        using var client = await LoginAsync(data.Teacher);

        var html = await HtmlAsync(client, IndexPath);

        Assert.Equal(title, RowTitle(Assert.Single(Rows(html))));
        Assert.Contains(courseTitle, WebUtility.HtmlDecode(html));
        Assert.Contains(lessonTitle, WebUtility.HtmlDecode(html));
        Assert.DoesNotContain(title, html);
        Assert.DoesNotContain(courseTitle, html);
        Assert.DoesNotContain(lessonTitle, html);
        var filtered = await HtmlAsync(client,
            QueryHelpers.AddQueryString(IndexPath, "Search", search));
        var input = Input(SearchForm(filtered), "Search");
        Assert.Equal(search, Attribute(input, "value"));
        Assert.DoesNotContain("\" autofocus onfocus=", input);
    }

    [Fact]
    public async Task CreateDraftLink_OpensExistingFormWithoutCreatingBusinessData()
    {
        var data = await SeedAsync();
        using var client = await LoginAsync(data.Teacher);
        var before = await CountsAsync();
        var html = await HtmlAsync(client, IndexPath);
        string create = Attribute(LinkByText(html, "+ Tạo bài luyện tập"), "href");

        var form = await HtmlAsync(client, create);

        Assert.Equal("/TeacherPractices/Create", Url(create).AbsolutePath);
        Assert.Contains("Tạo bài luyện tập nháp", WebUtility.HtmlDecode(form));
        Assert.Contains("name=\"Input.Title\"", form);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task CourseLink_OpensCorrectOwnedManagePageWithoutWriting()
    {
        var data = await SeedAsync();
        await AddPracticeAsync(data.Teacher.Id, "Owned Course link", lessonId: data.Own.LessonId);
        using var client = await LoginAsync(data.Teacher);
        var before = await CountsAsync();
        var html = await HtmlAsync(client, IndexPath);
        string manage = Attribute(Assert.Single(Links(Cells(Assert.Single(Rows(html)))[2])), "href");

        var courseHtml = await HtmlAsync(client, manage);

        Assert.StartsWith("/TeacherCourses/Manage", Url(manage).AbsolutePath);
        Assert.Contains(data.Own.CourseId.ToString(), manage);
        Assert.Contains(data.Own.CourseTitle, courseHtml);
        Assert.DoesNotContain(data.Other.CourseTitle, courseHtml);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task ReadingFilteringAndPaging_DoNotCreateAttemptsResultsOrChangeExistingPractice()
    {
        var data = await SeedAsync();
        var practiceId = await AddPracticeAsync(data.Teacher.Id, "Read-only grammar",
            lessonId: data.Own.LessonId, questionCount: 2);
        using var client = await LoginAsync(data.Teacher);
        var before = await CountsAsync();

        await HtmlAsync(client, IndexPath);
        await HtmlAsync(client, FilterPath("grammar", PracticeStatus.Draft, data.Own.CourseId, 1, 1));
        await HtmlAsync(client, FilterPath("grammar", PracticeStatus.Draft, data.Own.CourseId, int.MaxValue, 1));

        Assert.Equal(before, await CountsAsync());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Practices.AsNoTracking().SingleAsync(practice => practice.Id == practiceId);
        Assert.Equal(data.Teacher.Id, stored.OwnerTeacherUserId);
        Assert.Equal("Read-only grammar", stored.Title);
        Assert.Equal(PracticeStatus.Draft, stored.Status);
        Assert.Equal(data.Own.LessonId, stored.LessonId);
        Assert.Null(stored.ModuleId);
        Assert.Equal(CreatedAt, stored.CreatedAtUtc);
        Assert.Null(stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task ForgedOwnerQueryParameters_DoNotReplaceAuthenticatedTeacher()
    {
        var data = await SeedAsync();
        await AddPracticeAsync(data.Teacher.Id, "OWN_SPOOF_TEST");
        await AddPracticeAsync(data.OtherTeacher.Id, "FOREIGN_SPOOF_TEST");
        using var client = await LoginAsync(data.Teacher);
        string path = QueryHelpers.AddQueryString(IndexPath, new Dictionary<string, string?>
        {
            ["actorUserId"] = data.OtherTeacher.Id.ToString(),
            ["OwnerTeacherUserId"] = data.OtherTeacher.Id.ToString(),
            ["CourseOptions[0].Text"] = "FORGED_COURSE_LABEL"
        });

        var html = await HtmlAsync(client, path);

        Assert.Equal("OWN_SPOOF_TEST", RowTitle(Assert.Single(Rows(html))));
        Assert.DoesNotContain("FOREIGN_SPOOF_TEST", html);
        Assert.DoesNotContain("FORGED_COURSE_LABEL", html);
    }

    [Theory]
    [InlineData(CourseStatus.Draft)]
    [InlineData(CourseStatus.Published)]
    [InlineData(CourseStatus.Unpublished)]
    [InlineData(CourseStatus.Archived)]
    public async Task TeacherManagementList_IncludesHiddenParentsAndAnyOwnedCourseStatus(CourseStatus status)
    {
        var data = await SeedAsync();
        await AddPracticeAsync(data.Teacher.Id, "MANAGEMENT_DRAFT", lessonId: data.Own.LessonId);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Courses.SingleAsync(course => course.Id == data.Own.CourseId)).Status = status;
            (await db.Modules.SingleAsync(module => module.Id == data.Own.ModuleId)).Visibility = ModuleVisibility.Hidden;
            (await db.Lessons.SingleAsync(lesson => lesson.Id == data.Own.LessonId)).Status = LessonStatus.Hidden;
            await db.SaveChangesAsync();
        }
        using var client = await LoginAsync(data.Teacher);

        var html = await HtmlAsync(client, IndexPath + "?CourseId=" + data.Own.CourseId);

        Assert.Equal("MANAGEMENT_DRAFT", RowTitle(Assert.Single(Rows(html))));
        AssertSelected(html, "CourseId", data.Own.CourseId.ToString());
    }

    private async Task<HttpClient> LoginAsync(ApplicationUser user)
    {
        var client = IdentityTestHelpers.CreateClient(_factory);
        using var response = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static async Task<string> HtmlAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static Uri Url(string href) => new Uri(new Uri("http://localhost"), href);
    private static Dictionary<string, Microsoft.Extensions.Primitives.StringValues> Query(string href) =>
        QueryHelpers.ParseQuery(Url(href).Query);
    private static bool IsIndexUrl(string href) =>
        Url(href).AbsolutePath == IndexPath || Url(href).AbsolutePath == IndexPath + "/Index";

    private static string FilterPath(string search, PracticeStatus status, Guid courseId, int page, int size) =>
        QueryHelpers.AddQueryString(IndexPath, new Dictionary<string, string?>
        {
            ["Search"] = search, ["Status"] = status.ToString(), ["CourseId"] = courseId.ToString(),
            ["PageNumber"] = page.ToString(), ["PageSize"] = size.ToString()
        });

    private static void AssertFilters(string href, string search, PracticeStatus status, Guid courseId, int page, int size)
    {
        Assert.True(IsIndexUrl(href));
        var query = Query(href);
        Assert.Equal(search, query["Search"].ToString());
        Assert.Equal(status.ToString(), query["Status"].ToString());
        Assert.Equal(courseId.ToString(), query["CourseId"].ToString());
        Assert.Equal(page.ToString(), query["PageNumber"].ToString());
        Assert.Equal(size.ToString(), query["PageSize"].ToString());
        Assert.DoesNotContain(query.Keys, key => key.StartsWith("Query.", StringComparison.OrdinalIgnoreCase));
    }

    private static string Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, "\\s" + Regex.Escape(name) + "=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : string.Empty;
    }

    private static string Text(string html) => Regex.Replace(
        WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", " ")), "\\s+", " ").Trim();
    private static List<string> Links(string html) => Regex.Matches(html, "<a\\b[^>]*>.*?</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline).Select(match => match.Value).ToList();
    private static string LinkByText(string html, string text) => Assert.Single(Links(html), link => Text(link) == text);

    private static string SearchForm(string html) => Assert.Single(
        Regex.Matches(html, "<form\\b[^>]*>.*?</form>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(match => match.Value), form =>
            Attribute(form[..(form.IndexOf('>') + 1)], "method").Equals("get", StringComparison.OrdinalIgnoreCase)
            && IsIndexUrl(Attribute(form[..(form.IndexOf('>') + 1)], "action")));

    private static string Input(string html, string name) => Assert.Single(
        Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase).Select(match => match.Value),
        input => Attribute(input, "name") == name);

    private static List<string> OptionTags(string html, string name)
    {
        var select = Assert.Single(Regex.Matches(html, "<select\\b[^>]*>.*?</select>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline).Select(match => match.Value),
            item => Attribute(item[..(item.IndexOf('>') + 1)], "name") == name);
        return Regex.Matches(select, "<option\\b[^>]*>", RegexOptions.IgnoreCase)
            .Select(match => match.Value).ToList();
    }

    private static bool IsSelected(string option) => Regex.IsMatch(option, "\\sselected(?:=|\\s|>)", RegexOptions.IgnoreCase);
    private static void AssertSelected(string html, string name, string value)
    {
        var options = OptionTags(html, name);
        var selected = Assert.Single(options, IsSelected);
        Assert.Equal(value, Attribute(selected, "value"));
    }

    private static List<string> Rows(string html)
    {
        var body = Regex.Match(html, "<tbody\\b[^>]*>(.*?)</tbody>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return Regex.Matches(body.Value, "<tr\\b[^>]*>.*?</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(match => match.Value).ToList();
    }

    private static List<string> Cells(string row) => Regex.Matches(row, "<td\\b[^>]*>(.*?)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline).Select(match => match.Groups[1].Value).ToList();
    private static string RowTitle(string row) => Text(Assert.Single(Links(Cells(row)[0])));
    private static List<string> PaginationBlocks(string html) => Regex.Matches(html, "<nav\\b[^>]*>.*?</nav>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline).Select(match => match.Value)
        .Where(nav => Attribute(nav[..(nav.IndexOf('>') + 1)], "aria-label") == "Phân trang bài luyện tập").ToList();
    private static string Pagination(string html) => Assert.Single(PaginationBlocks(html));

    private async Task<Data> SeedAsync()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        var other = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var own = AddCourse(db, teacher.Id, "OWN_" + Guid.NewGuid().ToString("N"));
        var second = AddCourse(db, teacher.Id, "SECOND_" + Guid.NewGuid().ToString("N"));
        var foreign = AddCourse(db, other.Id, "FOREIGN_" + Guid.NewGuid().ToString("N"));
        await db.SaveChangesAsync();
        return new Data(teacher, other, own, second, foreign);
    }

    private static Parent AddCourse(AppDbContext db, Guid teacherId, string prefix)
    {
        var course = new Course { OwnerTeacherUserId = teacherId, Title = prefix + "_COURSE" };
        var module = new Module { CourseId = course.Id, Title = prefix + "_MODULE", OrderIndex = 0 };
        var lesson = new Lesson
        {
            ModuleId = module.Id, Title = prefix + "_LESSON", OrderIndex = 0, Status = LessonStatus.Published
        };
        lesson.Resources.Add(new LessonResource
        {
            ResourceType = LessonResourceType.Text, Title = "Text resource",
            ContentText = "Lesson content", OrderIndex = 0
        });
        module.Lessons.Add(lesson);
        course.Modules.Add(module);
        db.Courses.Add(course);
        return new Parent(course.Id, module.Id, lesson.Id, course.Title, module.Title, lesson.Title);
    }

    private async Task<Guid> AddPracticeAsync(Guid teacherId, string title, Guid? lessonId = null,
        Guid? moduleId = null, PracticeStatus status = PracticeStatus.Draft,
        int questionCount = 0, DateTimeOffset? createdAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var practice = new Practice
        {
            OwnerTeacherUserId = teacherId, Title = title, Status = status,
            LessonId = lessonId, ModuleId = moduleId, CreatedAtUtc = createdAt ?? CreatedAt
        };
        for (int index = 0; index < questionCount; index++)
        {
            var question = new Question
            {
                OwnerTeacherUserId = teacherId, Content = "QUESTION_PRIVATE_CONTENT " + index,
                Explanation = "QUESTION_PRIVATE_EXPLANATION", PrimarySkill = EnglishSkill.Grammar,
                Status = QuestionStatus.Published
            };
            question.Options.Add(new QuestionOption
            {
                Content = "QUESTION_PRIVATE_CORRECT_OPTION", IsCorrect = true, OrderIndex = 0
            });
            question.Options.Add(new QuestionOption { Content = "Incorrect option", IsCorrect = false, OrderIndex = 1 });
            practice.Questions.Add(new PracticeQuestion { Question = question, OrderIndex = index });
        }
        db.Practices.Add(practice);
        await db.SaveChangesAsync();
        return practice.Id;
    }

    private async Task<Counts> CountsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return new Counts(await db.Practices.CountAsync(), await db.PracticeQuestions.CountAsync(),
            await db.PracticeSubmissions.CountAsync(), await db.PracticeAnswers.CountAsync(),
            await db.Attempts.CountAsync(), await db.AttemptAnswers.CountAsync(),
            await db.Questions.CountAsync(), await db.QuestionOptions.CountAsync(), await db.Courses.CountAsync());
    }

    private sealed record Parent(Guid CourseId, Guid ModuleId, Guid LessonId,
        string CourseTitle, string ModuleTitle, string LessonTitle);
    private sealed record Data(ApplicationUser Teacher, ApplicationUser OtherTeacher,
        Parent Own, Parent SecondOwn, Parent Other);
    private sealed record Counts(int Practices, int PracticeQuestions, int Submissions, int PracticeAnswers,
        int Attempts, int AttemptAnswers, int Questions, int Options, int Courses);
}
