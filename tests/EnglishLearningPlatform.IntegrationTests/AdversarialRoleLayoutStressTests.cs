using System.Net;
using EnglishLearningPlatform.Infrastructure.Identity;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

/// <summary>
/// Adversarial stress tests for Milestone 2 layout segregation and ViewStart resolution.
/// Covers all 4 roles across multiple endpoints:
/// - Student: /Learning/Dashboard, /Assessment, /Checkout/History, /Learning
/// - Teacher: /TeacherCourses, /TeacherQuestionBank
/// - Admin: /Admin/Transactions, /Admin/Users
/// - Guest: /, /Courses
/// </summary>
public sealed class AdversarialRoleLayoutStressTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData("/Learning/Dashboard")]
    [InlineData("/Assessment")]
    [InlineData("/Checkout/History")]
    [InlineData("/Learning")]
    public async Task StudentEndpoints_RenderStudentLayout_WithNoTeacherOrAdminChrome(string endpoint)
    {
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, student);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Positive assertions: Student chrome
        Assert.Contains("student-workspace", html);
        Assert.Contains("student-topbar", html);
        Assert.Contains("studio-nav-group", html);
        Assert.Contains("Estudy", html);
        Assert.Contains("Học viên", html);
        Assert.Contains("Bàn học", html);
        Assert.Contains("Khóa học của tôi", html);
        Assert.Contains("Đánh giá", html);
        Assert.Contains(student.FullName, html);

        // Negative assertions: Strict segregation - No teacher or admin links/chrome
        Assert.DoesNotContain("teacher-sidebar", html);
        Assert.DoesNotContain("Không gian giảng dạy", html);
        Assert.DoesNotContain("/TeacherCourses", html);
        Assert.DoesNotContain("/TeacherQuestionBank", html);
        Assert.DoesNotContain("Ngân hàng câu hỏi", html);
        Assert.DoesNotContain("admin-sidebar", html);
        Assert.DoesNotContain("Production Console", html);
        Assert.DoesNotContain("/Admin/Transactions", html);
        Assert.DoesNotContain("/Admin/Users", html);
    }

    [Theory]
    [InlineData("/TeacherCourses")]
    [InlineData("/TeacherQuestionBank")]
    public async Task TeacherEndpoints_RenderTeacherStudioLayout_WithNoStudentOrAdminChrome(string endpoint)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, teacher);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Positive assertions: Teacher Studio chrome
        Assert.Contains("teacher-studio", html);
        Assert.Contains("studio-shell", html);
        Assert.Contains("studio-sidebar", html);
        Assert.Contains("teacher-sidebar", html);
        Assert.Contains("Không gian giảng dạy", html);
        Assert.Contains("studio-topbar", html);
        Assert.Contains("/TeacherCourses", html);
        Assert.Contains("/TeacherQuestionBank", html);
        Assert.Contains("Ngân hàng câu hỏi", html);
        Assert.Contains(teacher.FullName, html);

        // Negative assertions: Strict segregation - No student or admin links/chrome
        Assert.DoesNotContain("student-workspace", html);
        Assert.DoesNotContain("dashboard-topbar-clean", html);
        Assert.DoesNotContain("/Learning/Dashboard", html);
        Assert.DoesNotContain("Bàn học", html);
        Assert.DoesNotContain("admin-sidebar", html);
        Assert.DoesNotContain("Production Console", html);
        Assert.DoesNotContain("/Admin/Transactions", html);
        Assert.DoesNotContain("/Admin/Users", html);
    }

    [Theory]
    [InlineData("/Admin")]
    [InlineData("/Admin/Audit")]
    [InlineData("/Admin/CreateTeacher")]
    [InlineData("/Admin/Transactions")]
    [InlineData("/Admin/Users")]
    public async Task AdminEndpoints_RenderAdminConsoleLayout_WithNoStudentOrTeacherChrome(string endpoint)
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Positive assertions: Admin Console chrome
        Assert.Contains("admin-body", html);
        Assert.Contains("admin-shell", html);
        Assert.Contains("admin-sidebar", html);
        Assert.Contains("aria-label=\"Điều hướng quản trị\"", html);
        Assert.Contains("aria-label=\"Thực đơn quản trị\"", html);
        Assert.Contains("aria-controls=\"admin-sidebar\"", html);
        Assert.Contains("admin-topbar", html);
        Assert.DoesNotContain("Hệ thống: Trực tuyến", html);
        Assert.DoesNotContain("Production Console", html);
        Assert.Contains("/Admin/Transactions", html);
        Assert.Contains("/Admin/Users", html);
        Assert.Contains("/Admin/Audit", html);
        Assert.Contains("Giao dịch", html);
        Assert.Contains("Người dùng", html);
        Assert.Contains(admin.FullName, html);

        // Negative assertions: Strict segregation - No student or teacher links/chrome
        Assert.DoesNotContain("student-workspace", html);
        Assert.DoesNotContain("dashboard-topbar-clean", html);
        Assert.DoesNotContain("/Learning/Dashboard", html);
        Assert.DoesNotContain("Bàn học", html);
        Assert.DoesNotContain("studio-sidebar", html);
        Assert.DoesNotContain("teacher-sidebar", html);
        Assert.DoesNotContain("Không gian giảng dạy", html);
        Assert.DoesNotContain("/TeacherCourses", html);
        Assert.DoesNotContain("/TeacherQuestionBank", html);
        Assert.DoesNotContain("Ngân hàng câu hỏi", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Courses")]
    public async Task GuestEndpoints_RenderPublicLayout_WithoutRoleChrome(string endpoint)
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Positive assertions: Public discovery
        Assert.Contains("Estudy", html);
        Assert.Contains("site-header", html);
        Assert.Contains("main-nav", html);
        Assert.Contains("Khóa học", html);

        // Negative assertions: Guest MUST NOT see any role workspaces or private links
        Assert.DoesNotContain("student-workspace", html);
        Assert.DoesNotContain("teacher-studio", html);
        Assert.DoesNotContain("admin-body", html);
        Assert.DoesNotContain("studio-sidebar", html);
        Assert.DoesNotContain("admin-sidebar", html);
        Assert.DoesNotContain("/TeacherCourses", html);
        Assert.DoesNotContain("/TeacherQuestionBank", html);
        Assert.DoesNotContain("Ngân hàng câu hỏi", html);
        Assert.DoesNotContain("/Admin", html);
        Assert.DoesNotContain("Quản trị", html);
        Assert.DoesNotContain("/Learning/Dashboard", html);
        Assert.DoesNotContain("Bàn học", html);
        Assert.DoesNotContain("/Checkout/History", html);
    }

    [Theory]
    [InlineData("/Learning/Dashboard")]
    [InlineData("/Assessment")]
    [InlineData("/Checkout/History")]
    [InlineData("/TeacherCourses")]
    [InlineData("/TeacherQuestionBank")]
    [InlineData("/Admin/Transactions")]
    [InlineData("/Admin/Users")]
    public async Task UnauthenticatedAccess_ToProtectedRoleEndpoints_RedirectsToLogin(string endpoint)
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Contains("/Account/Login", response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData(AppRoles.Student, "/TeacherCourses")]
    [InlineData(AppRoles.Student, "/Admin/Transactions")]
    [InlineData(AppRoles.Teacher, "/Learning/Dashboard")]
    [InlineData(AppRoles.Teacher, "/Admin/Transactions")]
    [InlineData(AppRoles.Admin, "/Learning/Dashboard")]
    [InlineData(AppRoles.Admin, "/TeacherCourses")]
    public async Task CrossRoleUnauthorizedAccess_IsRejectedWithAccessDenied(string role, string forbiddenEndpoint)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync(forbiddenEndpoint);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location.ToString());
    }
}
