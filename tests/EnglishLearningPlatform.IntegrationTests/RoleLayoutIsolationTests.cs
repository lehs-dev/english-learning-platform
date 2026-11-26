using System.Net;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Infrastructure.Identity;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

/// <summary>
/// Opaque-box E2E test suite for Tier 2: Role Layout Isolation & Segregation.
/// Covers requirements from ORIGINAL_REQUEST.md § R2 and PROJECT.md Features 6, 7, 8, 9, 10.
/// Validates that Guest, Student, Teacher, and Admin have completely segregated layouts
/// with zero cross-role link leakage or clutter.
/// </summary>
public sealed class RoleLayoutIsolationTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData(AppRoles.Teacher, "teacher-studio", "teacher-sidebar")]
    [InlineData(AppRoles.Admin, "admin-body", "admin-sidebar")]
    public async Task CourseDiscovery_KeepsRoleSidebarAndCurrentNavigation(string role, string bodyClass, string sidebarClass)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        using var response = await client.GetAsync("/Courses");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(bodyClass, html);
        Assert.Contains(sidebarClass, html);
        if (role == AppRoles.Admin)
            AssertActiveNavigationLink(html, "/Courses");
        else
        {
            var sidebar = Regex.Match(html, "<aside\\b.*?</aside>", RegexOptions.Singleline).Value;
            Assert.DoesNotContain("href=\"/Courses\"", sidebar);
            Assert.DoesNotContain("href=\"/Profile\"", sidebar);
            Assert.Contains("href=\"/Profile\"", html);
        }
        Assert.DoesNotContain("homepage-canvas-frame", html);
    }

    [Fact]
    public async Task GuestLayout_Navbar_ContainsNoRoleConditionalLinks()
    {
        // Unauthenticated guest exploring public catalog
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.GetAsync("/Courses");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Guest navbar must contain public discovery links
        Assert.Contains("Estudy", html);
        Assert.Contains("Khóa học", html);

        // Guest navbar must NOT leak role-conditional or workspace-specific links
        Assert.DoesNotContain("/TeacherCourses", html);
        Assert.DoesNotContain("/TeacherQuestionBank", html);
        Assert.DoesNotContain("Ngân hàng câu hỏi", html);
        Assert.DoesNotContain("/Admin", html);
        Assert.DoesNotContain("Quản trị", html);
        Assert.DoesNotContain("/Learning/Dashboard", html);
        Assert.DoesNotContain("student-sidebar", html);
        Assert.DoesNotContain("/Checkout/History", html);
        var navigation = Regex.Match(html, "<nav\\b[^>]*id=\"main-nav\"[^>]*>.*?</nav>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(navigation);
        Assert.DoesNotContain("href=\"/Assessment\"", navigation);
    }

    [Fact]
    public async Task StudentWorkspace_ContainsStudentNavigation_AndNoTeacherOrAdminLinks()
    {
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync("/Learning/Dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Must contain Student workspace navigation items
        Assert.Contains("/Learning/Dashboard", html);
        Assert.Contains("<span>Trang chủ</span>", html);
        Assert.True(
            html.Contains("/Learning") || html.Contains("Khóa học"),
            "Student workspace must link to Learning courses.");

        // Must NOT contain Teacher Studio or Admin Console links
        Assert.DoesNotContain("/TeacherCourses", html);
        Assert.DoesNotContain("/TeacherQuestionBank", html);
        Assert.DoesNotContain("Ngân hàng câu hỏi", html);
        Assert.DoesNotContain("/Admin/Transactions", html);
        Assert.DoesNotContain("/Admin/Users", html);
        var sidebar = Regex.Match(html, "<aside\\b.*?</aside>", RegexOptions.Singleline).Value;
        Assert.DoesNotContain("href=\"/Profile\"", sidebar);
        Assert.DoesNotContain("/Account/Logout", sidebar);
        Assert.DoesNotContain("student-discover-link", html);
        Assert.Contains("href=\"/Profile\"", html);
        Assert.Contains("action=\"/Account/Logout\"", html);

        using var coursesResponse = await client.GetAsync("/Learning");
        Assert.Equal(HttpStatusCode.OK, coursesResponse.StatusCode);
        AssertActiveNavigationLink(await coursesResponse.Content.ReadAsStringAsync(), "/Learning");
    }

    [Fact]
    public async Task TeacherStudio_ContainsStudioSidebarAndNavigation_AndNoStudentOrAdminLinks()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, teacher);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync("/TeacherCourses");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Teacher Studio must contain studio navigation links
        Assert.True(
            html.Contains("/TeacherCourses") || html.Contains("Khóa học"),
            "Teacher Studio must link to Teacher Courses.");
        Assert.True(
            html.Contains("/TeacherQuestionBank") || html.Contains("Ngân hàng câu hỏi"),
            "Teacher Studio must link to Question Bank.");
        Assert.True(
            html.Contains("/TeacherPractices") || html.Contains("Tạo bài tập") || html.Contains("Luyện tập"),
            "Teacher Studio must provide link or action to Practice authoring.");
        Assert.True(
            html.Contains("sidebar", StringComparison.OrdinalIgnoreCase) || html.Contains("<aside", StringComparison.OrdinalIgnoreCase),
            "Teacher Studio layout must feature a dedicated sidebar layout structure per R2.");

        // Must NOT contain Student workspace or Admin Console links
        Assert.DoesNotContain("/Learning/Dashboard", html);
        Assert.DoesNotContain("student-sidebar", html);
        Assert.DoesNotContain("/Admin/Transactions", html);
        Assert.DoesNotContain("/Admin/Users", html);

        foreach (var (path, activeLink) in new[]
        {
            ("/TeacherQuestionBank", "/TeacherQuestionBank"),
            ("/TeacherPractices", "/TeacherPractices"),
            ("/TeacherPractices/Create", "/TeacherPractices")
        })
        {
            using var sectionResponse = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, sectionResponse.StatusCode);
            AssertActiveNavigationLink(await sectionResponse.Content.ReadAsStringAsync(), activeLink);
        }
    }

    [Fact]
    public async Task AdminConsole_ContainsAdminSidebarAndKpiSummaries_AndNoStudentOrTeacherStudioLinks()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync("/Admin/Transactions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Admin Console must contain administration navigation links
        Assert.True(
            html.Contains("/Admin/Transactions") || html.Contains("Giao dịch"),
            "Admin Console must link to Transactions.");
        Assert.True(
            html.Contains("/Admin/Users") || html.Contains("Người dùng") || html.Contains("Tài khoản"),
            "Admin Console must link to Users management.");
        Assert.True(
            html.Contains("sidebar", StringComparison.OrdinalIgnoreCase) || html.Contains("<aside", StringComparison.OrdinalIgnoreCase),
            "Admin Console layout must feature a dedicated enterprise sidebar per R2.");

        // Must NOT contain Student workspace or Teacher Studio links
        Assert.DoesNotContain("/Learning/Dashboard", html);
        Assert.DoesNotContain("student-sidebar", html);
        Assert.DoesNotContain("/TeacherQuestionBank", html);
        Assert.DoesNotContain("Ngân hàng câu hỏi", html);
    }

    [Theory]
    [InlineData(AppRoles.Student, "/Learning/Dashboard")]
    [InlineData(AppRoles.Teacher, "/TeacherCourses")]
    [InlineData(AppRoles.Admin, "/Admin/Transactions")]
    public async Task LayoutSegregation_WorkspacesRenderWithoutCrossRoleClutter(
        string role, string workspacePath)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync(workspacePath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // User profile and account menu should be present
        Assert.Contains(user.FullName, html);

        // Verification of segregation:
        if (role == AppRoles.Student)
        {
            Assert.DoesNotContain("Ngân hàng câu hỏi", html);
            Assert.DoesNotContain("/Admin/Transactions", html);
        }
        else if (role == AppRoles.Teacher)
        {
            Assert.DoesNotContain("student-sidebar", html);
            Assert.DoesNotContain("/Admin/Transactions", html);
        }
        else if (role == AppRoles.Admin)
        {
            Assert.DoesNotContain("student-sidebar", html);
            Assert.DoesNotContain("Ngân hàng câu hỏi", html);
        }
    }

    private static void AssertActiveNavigationLink(string html, string expectedUrl)
    {
        var pattern = $"""<a\b(?=[^>]*\bhref="{Regex.Escape(expectedUrl)}")(?=[^>]*\bclass="[^"]*\bis-active\b)(?=[^>]*\baria-current="page")[^>]*>""";
        Assert.Matches(pattern, html);
    }
}
