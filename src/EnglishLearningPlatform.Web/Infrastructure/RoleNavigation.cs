using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EnglishLearningPlatform.Web.Infrastructure;

public static class RoleNavigation
{
    public const string StudentDashboard = "/Learning/Dashboard";
    public const string TeacherStudio = "/TeacherCourses";
    public const string AdminConsole = "/Admin/Transactions";
    public const string PublicLanding = "/";

    public static string GetWorkspaceUrl(string? role) => role switch
    {
        AppRoles.Student => StudentDashboard,
        AppRoles.Teacher => TeacherStudio,
        AppRoles.Admin => AdminConsole,
        _ => PublicLanding
    };

    public static bool HasRoleWorkspace(string? role) =>
        role is AppRoles.Student or AppRoles.Teacher or AppRoles.Admin;

    public static IActionResult RedirectToRoleWorkspace(this Controller controller, string? role) => role switch
    {
        AppRoles.Student => controller.RedirectToAction("Dashboard", "Learning"),
        AppRoles.Teacher => controller.RedirectToAction("Index", "TeacherCourses"),
        AppRoles.Admin => controller.RedirectToAction("Transactions", "Admin"),
        _ => controller.RedirectToAction("Index", "Home")
    };

    public static bool IsValidReturnUrl(IUrlHelper urlHelper, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return false;
        if (!urlHelper.IsLocalUrl(returnUrl)) return false;
        var pathOnly = returnUrl.Split('?', '#')[0].TrimEnd('/');
        if (string.IsNullOrEmpty(pathOnly) ||
            pathOnly.Equals("/Home", StringComparison.OrdinalIgnoreCase) ||
            pathOnly.Equals("/Home/Index", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}
