using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace EnglishLearningPlatform.Web.Models.Account;

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "Hãy nhập họ và tên.")]
    [StringLength(200, ErrorMessage = "Họ và tên dài tối đa 200 ký tự.")]
    [Display(Name = "Họ và Tên")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ thư điện tử.")]
    [EmailAddress(ErrorMessage = "Địa chỉ thư điện tử không hợp lệ.")]
    [StringLength(256, ErrorMessage = "Địa chỉ thư điện tử dài tối đa 256 ký tự.")]
    [Display(Name = "Thư điện tử")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [MinLength(8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    [Display(Name = "Xác nhận mật khẩu")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}
