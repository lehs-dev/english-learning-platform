namespace EnglishLearningPlatform.Application.Authorization;

/// <summary>
/// Tên các authorization policy của nền tảng. Dùng với
/// <c>[Authorize(Policy = AuthorizationPolicies.Teacher)]</c> hoặc
/// <c>IAuthorizationService</c>. Mọi policy role đều bao gồm
/// <see cref="ActiveRoleRequirement"/>: tài khoản phải có đúng một role đang hoạt động.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Đã đăng nhập và có đúng một role đang hoạt động.</summary>
    public const string SingleActiveRole = "SingleActiveRole";

    /// <summary>Role Student đang hoạt động.</summary>
    public const string Student = "Student";

    /// <summary>Role Teacher đang hoạt động.</summary>
    public const string Teacher = "Teacher";

    /// <summary>Role Admin đang hoạt động.</summary>
    public const string Admin = "Admin";
}
