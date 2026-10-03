# Profile và UI cơ bản

Phần này thêm thanh điều hướng Estudy, giao diện đăng nhập/đăng ký, menu avatar, profile và kiểm tra quyền Learning. Course và Learning hiện đã có giao diện thật; xem [Course authoring](course-authoring.md). Các tab Đề thi/Đánh giá/Giới thiệu/Dịch từ vẫn là khung giao diện. Ô tìm kiếm trên header còn disabled; dùng form tìm/lọc tại `/Courses`.

## Đọc code theo thứ tự

| File/thư mục | Trách nhiệm |
| --- | --- |
| `Web/Views/Shared/_Layout.cshtml` | Thanh điều hướng và khung trang chung. |
| `Web/ViewComponents/AccountMenuViewComponent.cs` | Lấy profile của user đang đăng nhập để render avatar/menu. |
| `Web/Views/Shared/Components/AccountMenu/Default.cshtml` | Menu có hai thao tác: xem profile, đăng xuất. Logout vẫn dùng POST và antiforgery token. |
| `Web/Views/Account/`, `Web/Views/Profile/` | Razor UI và hiển thị validation từ server. |
| `Web/Controllers/ProfileController.cs` | GET/POST profile, lấy UserId từ cookie/claims; chỉ bind FullName từ form. |
| `Application/Identity/IProfileService.cs`, `UserProfile.cs` | Contract và dữ liệu profile, không phụ thuộc EF/Identity. |
| `Infrastructure/Identity/IdentityProfileService.cs` | Đọc thông tin qua UserManager; cập nhật họ tên và UpdatedAtUtc. |
| `Web/wwwroot/css/site.css`, `account.css` | CSS chung và CSS account/profile; có breakpoint cho mobile. |
| `Web/wwwroot/js/site.js` | Đóng menu khi click ngoài/Escape, mở menu mobile, hiện/ẩn mật khẩu, ngày theo múi giờ trình duyệt. |

Các đường dẫn trong bảng tính từ `src/EnglishLearningPlatform.*`. Luồng profile: HTTP → ProfileController → IProfileService → IdentityProfileService → UserManager/EF → SQL Server → Razor view.

Profile hiển thị email, họ tên, role, trạng thái và ngày tham gia. Chỉ cho cập nhật họ tên; email, role, trạng thái và UserId gửi thêm từ client không được bind. Menu đọc dữ liệu hiện tại từ server nên tên mới xuất hiện ngay sau khi lưu.

## Tích hợp quyền vào Learning

`Application/Learning/ILearningAccessService.cs` định nghĩa contract; `Infrastructure/Learning/LearningAccessService.cs` đọc role, Course, Module, Lesson và Enrollment từ database. Ownership của Module/Lesson được suy ra từ Course cha.

| Tài khoản | Quyền nội dung Learning |
| --- | --- |
| Guest | Route protected chuyển tới Login; catalog `/Courses` và detail Published được xem công khai, không có nội dung Lesson/resource. |
| Student | Xem nội dung của Course đã enroll, Module Visible và Lesson Published. Không quản lý nội dung. |
| Teacher | Preview/quản lý Course, Module, Lesson thuộc chính mình. Không truy cập nội dung Teacher khác qua các route protected. |
| Admin | Không truy cập/quản lý nội dung qua route Learning. Profile vẫn sử dụng bình thường. |

Student đã enroll vẫn xem nội dung Course Unpublished/Archived; Course Draft bị chặn. `RecordProgress` chỉ cho Student đã enroll vào Lesson khả dụng, Course chưa Archived. Service này chỉ kiểm tra quyền, không tạo Enrollment hoặc thay đổi progress.

`LearningController` dùng `ICourseService`, kiểm tra quyền trước khi đọc/render `/Learning/Course/{id}`, `/Learning/Module/{id}`, `/Learning/Lesson/{id}`, `/Learning/Resource/{id}` và `/Learning/ManageCourse/{id}`. Trang Learning hiển thị cấu trúc và nội dung thật; Teacher quản lý tại `/TeacherCourses`. Kết quả service là Allowed, Forbidden hoặc NotFound. MVC cookie chuyển Forbidden tới `/Account/AccessDenied`, trang này trả HTTP 403; resource không tồn tại trả 404.

Khi thêm use case sửa nội dung hoặc ghi progress, gọi `CheckAccessAsync` với operation phù hợp ở server trước thao tác và thực thi các quy tắc/transaction của use case. `ManageContent` kiểm tra role và ownership; validation publish/lifecycle vẫn thuộc module Learning.

## Chạy và kiểm thử

Chạy ứng dụng theo README; không cần migration mới cho thay đổi này. Teacher/Admin dùng tài khoản được cấp sẵn; đăng ký công khai vẫn luôn tạo Student.

Nếu dùng SQL Server cài trực tiếp trên Windows và đăng nhập bằng Windows Authentication, chạy profile local sau. Profile này luôn trỏ tới database gốc `EnglishLearningDb`:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Server=localhost;Database=EnglishLearningDb;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=true'
dotnet ef database update --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web
dotnet run --project .\src\EnglishLearningPlatform.Web --launch-profile LocalSqlServer
```

EF CLI không đọc launch profile. Cần connection string ở biến môi trường cho bước cập nhật database; profile `LocalSqlServer` chỉ cấu hình lệnh chạy web.

Mở `http://localhost:5098`. Database mới cần được áp dụng các migration hiện có như README trước lần chạy đầu tiên. Profile `http` nhận connection string và URL từ environment như cấu hình Docker/Dev Container; chỉ profile `LocalSqlServer` mới đặt Windows Authentication và cổng 5098. Docker Compose chạy DLL đã publish nên không đọc launch profile.

Đăng ký mới được lưu trong database của connection string đang sử dụng và luôn tạo Student. Profile không tạo database hay tài khoản demo; Teacher/Admin dùng tài khoản được cấp trong database đó. Phần UI/profile/quyền Learning không thay đổi schema và không cần migration mới.

```powershell
dotnet test EnglishLearningPlatform.sln
```

Integration tests mặc định dùng SQL Server Testcontainers nên cần Docker. Nếu dùng SQL Server local có quyền tạo database, có thể chọn server cho test:

```powershell
$env:ELP_TEST_SQLSERVER = 'Server=localhost;Integrated Security=True;TrustServerCertificate=True'
dotnet test EnglishLearningPlatform.sln
Remove-Item Env:ELP_TEST_SQLSERVER
```

Fixture luôn thay tên database bằng `EnglishLearningPlatformTests_<GUID>` rồi tự dọn database của chính nó. Database ứng dụng không được dùng làm database test.

Tests kiểm tra login/profile/logout cho cả ba role, đăng ký chỉ tạo Student, chống sửa profile người khác/role/email, CSRF, Locked/Disabled, redirect đăng nhập, ownership Course/Module/Lesson, enrollment, visibility và Course lifecycle. UI được kiểm tra thêm bằng trình duyệt ở desktop/tablet/mobile.
