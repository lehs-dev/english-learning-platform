using System.ComponentModel.DataAnnotations;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Web.Models.Admin;

public class CreateTeacherInput
{
    [Required, StringLength(200, MinimumLength = 2), Display(Name = "Họ tên")]
    public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 8), DataType(DataType.Password), Display(Name = "Mật khẩu ban đầu")]
    public string Password { get; set; } = "";
}
public sealed class CreateAdminUserInput : CreateTeacherInput
{
    [Required, RegularExpression("Student|Teacher|Admin"), Display(Name = "Vai trò")]
    public string Role { get; set; } = "Student";
}
public sealed class UpdateAdminUserInput
{
    public Guid UserId { get; set; }
    [Required] public string Role { get; set; } = "";
    public AccountStatus Status { get; set; }
}
