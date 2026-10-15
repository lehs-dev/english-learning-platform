# Admin dashboard — phần việc Dương

Nhánh: `feature/duong-admin-dashboard`. Kiểm tra ngày 14/10/2026.
Nhánh được tạo từ nhánh Attempt đang chờ review. Nếu PR Attempt chưa merge,
PR Admin cần đặt base là nhánh Attempt để chỉ review thay đổi Admin; sau đó chuyển base về main.

## Bạn đang làm gì?

Bạn hoàn thiện quản trị tài khoản và nhật ký thao tác. Repo trước thay đổi đã có
tra cứu User và giao dịch; phần bổ sung là tổng quan, lọc role, tạo Teacher,
đổi role, Lock/Unlock/Disable và ghi/xem audit. Admin không sửa Course, câu hỏi
hay đáp án Assessment; màn hình giao dịch vẫn chỉ đọc.

## Luồng một thao tác

1. `/Admin/Users`: chọn “Chỉnh sửa” trên tài khoản để mở hộp thoại, chọn vai trò/trạng thái và lưu.
2. Form POST gửi `UserId`, `Role`, `Status` và token chống CSRF đến `AdminController.UpdateUser`.
3. Controller kiểm tra dữ liệu; `AdminManagementService` kiểm tra lại quyền Admin từ database.
4. Service mở transaction và khóa `ELP.AdminManagement` bằng SQL Server `sp_getapplock`.
   Mọi thao tác Admin dùng cùng khóa, nên hai Admin không thể cùng vượt qua kiểm tra
   “còn Admin khác” rồi đồng thời hạ quyền cả hai.
5. Identity cập nhật role; service cập nhật trạng thái và ghi AuditLog trước/sau.
   Cùng transaction bảo đảm lỗi gán role hoặc ghi audit sẽ rollback toàn bộ.
6. Redirect về danh sách và hiển thị thông báo. Cookie cũ bị kiểm tra role/trạng thái
   ở request kế tiếp bởi `AccountStatusCookieEvents` đã có trong repo.

Tạo Teacher dùng `UserManager.CreateAsync`: mật khẩu được hash theo Identity,
không lưu mật khẩu rõ vào audit. Vai trò được gán cố định Teacher, không tin role
do client gửi. Form lỗi không điền lại mật khẩu.

Mở khóa là đặt Active **và** xóa `LockoutEnd`/`AccessFailedCount` của Identity,
kể cả khi trạng thái ứng dụng đã là Active. Disable giữ tài khoản và dữ liệu lịch sử,
không xóa User. Muốn kích hoạt lại thì chọn Đang hoạt động.

## Thử trên web demo

Mở `http://localhost:8081/Admin`, đăng nhập `admin1@demo.example.test` với mật khẩu
`LearningDemo__Password` trong `.env.uat` (không commit file này).

- Tổng quan: số User, Locked, Disabled và đơn giao dịch.
- Tài khoản: tìm theo tên/email, lọc role/trạng thái, phân trang.
- Tạo Teacher: thử email mới, email trùng, mật khẩu không hợp lệ.
- Sửa tài khoản thử: Teacher → Student, Locked → Active → Disabled; đăng nhập tài khoản
  đó để thấy quyền/trạng thái áp dụng. Không dùng tài khoản thật cho việc diễn tập.
- Audit: tìm email tài khoản vừa sửa để đối chiếu người thao tác và thay đổi trước/sau.
- Responsive: mở DevTools, viewport 390px; danh sách chuyển thành thẻ có nhãn từng trường.

## Cập nhật giao diện 15/10/2026

Thanh điều hướng bốn mục dùng chung; thẻ thống kê liên kết đến bộ lọc tương ứng;
bộ lọc đặt theo thứ tự tìm kiếm, vai trò, trạng thái, nút tìm kiếm/đặt lại.
Thông tin tên và email cùng một ô, badge role/trạng thái, cột thao tác riêng.
Hộp thoại native dialog hỗ trợ Tab/Escape, Hủy không ghi dữ liệu và bỏ lựa chọn chưa lưu.
Nhật ký hiển thị tiếng Việt theo Trước/Sau thay cho JSON.
Form tạo Teacher có phần hướng dẫn bên cạnh và gộp thành một cột trên điện thoại.
Browser UAT kiểm tra thêm hủy hộp thoại, reset form và Escape trên màn hình nhỏ.

## Kiểm thử

16 test tích hợp Admin (4 test mới + 12 test tra cứu/quyền hiện có): tạo Teacher/hash/role/audit,
email trùng, role/status, phiên đăng nhập cũ, Identity lockout, CSRF, quyền Student,
lọc role và hai Admin tự hạ quyền đồng thời; còn đúng một Admin và một audit thành công.

```powershell
dotnet test tests/EnglishLearningPlatform.IntegrationTests --filter "FullyQualifiedName~AdminManagementTests|FullyQualifiedName~AdminReadOnlyTests" --logger "trx;LogFileName=duong-admin.trx" -- RunConfiguration.CollectSourceInformation=false
```

Test mặc định tạo SQL Server container riêng. Với `ELP_TEST_SQLSERVER` cấu hình qua
môi trường, mỗi fixture vẫn tạo/xóa database test ngẫu nhiên, không dùng database ứng dụng.

Chromium `npm run test:admin` kiểm tra 5 nhóm: dashboard; tạo/tìm/lọc/khóa Teacher;
mở khóa/vô hiệu hóa/kích hoạt/đổi role; audit; responsive 5 trang 390px.
Script tạo tài khoản thử mới mỗi lần và chỉ chạy trên database demo riêng.
Truyền `LearningDemo__Password` và `ELP_UAT_URL` qua môi trường trước khi chạy.

Ảnh/report: `tests/browser/TestResults/admin-uat/`; log integration:
`tests/EnglishLearningPlatform.IntegrationTests/TestResults/duong-admin.trx`.
Không cần migration mới: dùng bảng AuditLogs, Users, Roles có sẵn.
Audit ghi thay đổi thành công của phần quản trị này; không phải nhật ký toàn bộ request.
