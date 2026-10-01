using Microsoft.AspNetCore.Authorization;

namespace EnglishLearningPlatform.Application.Authorization;

/// <summary>
/// Yêu cầu tài khoản có đúng một role đang hoạt động trong ba role
/// Student / Teacher / Admin. Đây là cách thực thi quy tắc
/// "mỗi tài khoản có một role đang hoạt động" ở tầng authorization.
/// </summary>
/// <remarks>
/// Handler đọc role từ claims của principal hiện tại. Role claims được tạo
/// lúc đăng nhập từ role trong DB và làm mới khi principal được cấp lại
/// (đăng nhập lại, refresh sign-in, hoặc revalidate theo chu kỳ cookie).
/// Nếu sau này có chức năng đổi role, cần đảm bảo principal được cấp lại
/// (ví dụ: cập nhật security stamp rồi revalidate), nếu không user giữ
/// role claims cũ đến lần đăng nhập tiếp theo.
/// </remarks>
public sealed class ActiveRoleRequirement : IAuthorizationRequirement;
