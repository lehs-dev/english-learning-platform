using System.ComponentModel.DataAnnotations;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Web.Models.Account;

public sealed class ProfileViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
    [StringLength(200, ErrorMessage = "Họ và tên không được vượt quá 200 ký tự.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public AccountStatus AccountStatus { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public string Initial => string.IsNullOrWhiteSpace(FullName)
        ? "?" : System.Globalization.StringInfo.GetNextTextElement(FullName.Trim()).ToUpperInvariant();

    public string RoleLabel => Role switch
    {
        "Student" => "Học viên",
        "Teacher" => "Giáo viên",
        "Admin" => "Quản trị viên",
        _ => "Chưa có vai trò"
    };

    public string StatusLabel => AccountStatus switch
    {
        AccountStatus.Active => "Đang hoạt động",
        AccountStatus.Locked => "Đã khóa",
        AccountStatus.Disabled => "Đã vô hiệu hóa",
        _ => "Không xác định"
    };

    public static ProfileViewModel FromProfile(UserProfile profile) => new()
    {
        FullName = profile.FullName,
        Email = profile.Email,
        Role = profile.Role,
        AccountStatus = profile.AccountStatus,
        CreatedAtUtc = profile.CreatedAtUtc
    };
}
