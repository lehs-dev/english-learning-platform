# Task 02 — Admin Dashboard Query Service

Ngày thực hiện: 24/10/2026. Nhánh: `feature/duong-admin-dashboard-query`.

## Dương đang làm gì?

Tách truy vấn tổng quan khỏi Controller và khỏi dịch vụ danh sách/phân trang. Dashboard cũ tải từng trang User/Order chỉ để lấy tổng số; service mới gửi trực tiếp các phép đếm đến SQL Server. Bổ sung hai danh sách chỉ đọc để đối chiếu số liệu: khóa học và lượt đăng ký/hoàn thành.

Luồng: `GET /Admin` → policy Admin → `AdminController.Index` → `IAdminDashboardQueryService.GetAsync` → `AdminDashboardQueryService` → EF Core/SQL Server → `AdminDashboardSummary` → `Views/Admin/Index.cshtml`.

Luồng xem chi tiết: liên kết từ chỉ số → `GET /Admin/Courses` hoặc `/Admin/Enrollments` → `IAdminOverviewDetailQueryService` → `AdminOverviewDetailQueryService` → DTO/phân trang → View. Các service cùng kiểm tra active Admin từ database, kể cả khi được gọi ngoài MVC.

## Quy tắc thống kê

| Chỉ số | Cách tính |
| --- | --- |
| User / Locked / Disabled | Đếm User và từng trạng thái tài khoản; giữ chức năng cũ |
| Student / Teacher | User có role tương ứng trong Identity, gồm cả Locked/Disabled; không đếm Admin vào hai nhóm |
| Tổng Course | Tất cả Course, gồm cả Unpublished và Archived |
| Phân bố trạng thái Course | Đủ bốn nhóm `Published`, `Draft`, `Unpublished`, `Archived`; `GroupBy` và `Count` thực hiện trên SQL Server. Đây là trạng thái lưu, không phải kiểm tra toàn bộ điều kiện người học có thể truy cập |
| Enrollment | Tổng bản ghi đăng ký, không phải số học viên duy nhất hoặc số lượt mở bài |
| Hoàn thành | Enrollment có `CompletedAtUtc`; giữ mốc hoàn thành lịch sử kể cả Course Archived, không suy lại từ phần trăm Lesson |
| Tỷ lệ hoàn thành | `CompletedEnrollments / Enrollments × 100`, làm tròn một chữ số thập phân; `null` khi chưa có lượt đăng ký, không chia cho 0 và không hiển thị 0% gây hiểu nhầm |
| Chưa hoàn thành | Tổng lượt đăng ký trừ lượt có `CompletedAtUtc`; liên kết mở danh sách lọc `completed=false` |
| Order | Tổng đơn hàng |
| Order thành công | Order có ít nhất một Payment Succeeded, cùng nguyên tắc ưu tiên thành công của CheckoutOrderService |

`orders.CountAsync(o => o.Payments.Any(...))` đếm Order, không đếm Payment. Một Order có nhiều lần thử hoặc nhiều Payment Succeeded vẫn chỉ đóng góp 1. Task 02 không tính doanh thu và không sửa xác nhận thanh toán.

Enrollment ở đây là số bản ghi lịch sử. Đây không phải kết quả kiểm tra quyền học hiện tại qua `LearningQueries.Valid()`.

Dashboard có liên kết nhỏ “Xem học viên” / “Xem giáo viên” dẫn đến `/Admin/Users?role=Student` / `Teacher`. Không kèm trạng thái hoặc khoảng ngày để danh sách bao gồm cùng nhóm tài khoản với chỉ số tổng. Integration test kiểm tra URL, role được chọn sẵn và số kết quả.

Dashboard chia bốn khu vực: **Người dùng**, **Đăng ký và hoàn thành**, **Khóa học**, **Thanh toán**. Phân bố khóa học dùng thanh tỷ lệ theo trạng thái; tỷ lệ hoàn thành dùng thanh tiến độ và giải thích khi dữ liệu rỗng. Các liên kết khóa học/đăng ký mang sẵn điều kiện lọc, không cần bấm Tìm kiếm thêm.

## Hai danh sách chỉ đọc

| Trang | Điều kiện lọc | Dữ liệu hiển thị |
| --- | --- | --- |
| `/Admin/Courses` | `search`: tên/mã khóa học, tên/email giáo viên; `status`: bốn trạng thái Course; `page` | Khóa học, giáo viên, trạng thái, ngày tạo, tổng đăng ký và hoàn thành |
| `/Admin/Enrollments` | `search`: tên/mã lượt đăng ký, tên/email học viên hoặc tên khóa; `completed`: `true`/`false` hoặc bỏ trống; `courseId`: khóa cụ thể; `page` | Học viên, khóa học, ngày đăng ký và ngày hoàn thành |

- Mỗi trang tối đa 20 dòng. Tìm kiếm, lọc, đếm, sắp xếp và `Skip`/`Take` chạy trên SQL Server trước khi tải DTO.
- Sắp xếp theo thời gian mới nhất rồi ID để ổn định khi nhiều dòng có cùng thời điểm; trang ngoài phạm vi được đưa về trang hợp lệ.
- Giữ toàn bộ điều kiện lọc khi chuyển trang. Từ danh sách khóa học có thể mở lượt đăng ký/hoàn thành của riêng khóa đó.
- DTO chỉ chiếu các trường cần hiển thị. Không tải toàn bộ Course/Enrollment hoặc dùng `Include` để tính số lượng trên bộ nhớ.
- Giữ số liệu lịch sử, gồm khóa Archived/Unpublished và tài khoản bị khóa/vô hiệu hóa; không lẫn với quyền truy cập học hiện tại.
- Chỉ active Admin được xem. Guest phải đăng nhập; Student/Teacher bị từ chối; cookie Admin cũ cũng mất quyền sau khi tài khoản bị khóa/vô hiệu hóa. Response Admin có `NoStore`.
- Giá trị trạng thái, boolean, ID hoặc số trang sai định dạng trả HTTP 400. Khóa không có đăng ký trả danh sách rỗng.
- Không có chức năng sửa/xóa khóa học hay đăng ký từ các trang này; quyền quản lý nội dung của Teacher giữ nguyên.

## Các file chính

- Application: `Administration/IAdminDashboardQueryService.cs` — hợp đồng và DTO chứa số liệu.
- Application: `Administration/IAdminOverviewDetailQueryService.cs` — hợp đồng, `AdminCourseOverview` và `AdminEnrollmentOverview`.
- Infrastructure: `Administration/AdminDashboardQueryService.cs` — truy vấn và kiểm tra active Admin.
- Infrastructure: `Administration/AdminOverviewDetailQueryService.cs` — lọc, phân trang và chiếu DTO hai danh sách chỉ đọc.
- Infrastructure: `DependencyInjection.cs` — đăng ký interface/implementation bằng `AddScoped`.
- Web: `Controllers/AdminController.cs` — gọi service, trả Forbid khi service từ chối.
- Web: `Models/Admin/AdminInputs.cs` — bỏ DTO Dashboard cũ không còn được dùng.
- Web: `Views/Admin/Index.cshtml`, `Courses.cshtml`, `Enrollments.cshtml`, `wwwroot/css/admin.css` — Dashboard theo nhóm, các danh sách và bố cục responsive.
- Web: `Views/Shared/_LayoutAdmin.cshtml` — thêm điều hướng đến hai trang, giữ một mục hiện hành.
- Tests: `AdminDashboardQueryTests.cs` — dữ liệu rỗng, nhiều trạng thái, retry thanh toán và phân quyền.
- Tests: `AdminOverviewDetailTests.cs` — lọc kết hợp/phân trang, số liệu lịch sử, dữ liệu rỗng, query sai và phân quyền.
- Tests: `AdversarialRoleLayoutStressTests.cs`, `SecurityConfigurationTests.cs` — bổ sung hai endpoint GET vào kiểm tra layout và bề mặt quyền; không nới lỏng yêu cầu với mutation.
- Browser: `tests/browser/admin-dashboard-uat.mjs` — kiểm tra chỉ đọc liên kết từ chỉ số, số kết quả, responsive và menu.

## Cần giải thích khi báo cáo

**DI:** MVC nhận interface qua constructor. AddScoped cung cấp implementation trong scope request, dùng chung DbContext scoped.

**Controller:** chỉ xử lý request/response; không chứa LINQ thống kê. Service có thể gọi trực tiếp trong test và vẫn kiểm tra quyền từ database.

**EF Core:** `CountAsync`, `Any` và `GroupBy` được dịch thành COUNT/EXISTS/GROUP BY ở SQL Server. Phép tổng hợp đăng ký theo khóa nằm trong projection SQL; không dùng ToList để tải toàn bộ rồi đếm trong C#. Tỷ lệ phần trăm tính từ hai số tổng hợp đã nhận, không phải từ toàn bộ bản ghi.

**AsNoTracking:** thích hợp cho truy vấn chỉ đọc. Phép COUNT vốn không materialize entity; AsNoTracking thể hiện rõ mục đích truy vấn.

**Async:** truyền CancellationToken và await tuần tự. Không chạy Task.WhenAll trên cùng DbContext vì context không hỗ trợ nhiều truy vấn đồng thời.

**Số liệu rỗng:** cần 1 active Admin để được phép xem; Student/Teacher và các bảng nghiệp vụ có thể rỗng, lúc đó chỉ số tương ứng bằng 0.

## Kiểm thử

Tổng cộng **15 test mới**: 6 test Dashboard ban đầu và 9 test cho danh sách chi tiết. Ngoài ra mở rộng kiểm tra layout thêm hai endpoint GET.

- Dashboard: dữ liệu rỗng rồi hỗn hợp, đủ bốn trạng thái khóa học, tỷ lệ hoàn thành, Student/Teacher, Admin Locked/Disabled và Guest.
- Danh sách: 21 dòng hợp lệ để kiểm tra trang 1/trang 2, không trùng dòng và không mất điều kiện search/status/completed/courseId; số đăng ký theo từng khóa được đếm chính xác.
- Dữ liệu lịch sử: giữ đăng ký của học viên bị khóa và khóa Archived; kiểm tra mốc hoàn thành thay vì suy từ trạng thái khóa.
- Empty và validation: danh sách rỗng có một trang hợp lệ, query sai bị từ chối; Dashboard không hiển thị thanh tiến độ khi chưa có đăng ký.
- Bảo mật: Guest, Student, Teacher, actor không tồn tại, Admin bị khóa/vô hiệu hóa và cookie đã đăng nhập trước đó.
- Các test dữ liệu rỗng có fixture SQL Server riêng, không phụ thuộc thứ tự chạy test. Database dùng SQL Server thật qua hạ tầng integration test hiện có, không thay bằng EF InMemory.

Dataset hỗn hợp: 5 User, 2 Student, 2 Teacher, 1 Locked, 1 Disabled; 4 Course với đủ 4 trạng thái; 3 Enrollment, 2 hoàn thành; 4 Order, 1 Order thành công dù có nhiều Payment. Test HTTP xác nhận các số được render ra Dashboard và response không cache.

Lệnh hồi quy:

```powershell
dotnet test tests/EnglishLearningPlatform.IntegrationTests --filter "FullyQualifiedName~AdminDashboard|FullyQualifiedName~AdminOverview|FullyQualifiedName~AdminReadOnlyTests|FullyQualifiedName~AdminManagementTests|FullyQualifiedName~AdversarialRoleLayoutStressTests|FullyQualifiedName~SecurityConfigurationTests"
```

Kết quả local: **65/65 test liên quan đạt**, gồm 15 test mới và hai trường hợp layout mới. Sau đó bổ sung assertion HTTP về trạng thái Dashboard rỗng và chạy riêng `AdminOverviewEmptyTests`: **1/1 đạt**. Không thay cho kết quả toàn bộ suite hoặc CI của commit sau này.

## Kiểm tra trình duyệt chỉ đọc

Script dùng Chromium/Playwright, không tạo khóa học, đăng ký hay giao dịch. Script đăng nhập bằng tài khoản Admin local được truyền qua environment, đối chiếu số liệu từ Dashboard với danh sách đã lọc, kiểm tra ba trang ở 320/390/768/1440px, menu mở/đóng trên mobile và lỗi JavaScript.

Chạy từ thư mục gốc dự án sau khi cài dependency trình duyệt theo `tests/browser/README.md` và ứng dụng local đã chạy:

```powershell
$env:ELP_UAT_URL = 'http://localhost:8080'
$env:ELP_ADMIN_EMAIL = Read-Host 'Email Admin local'
$adminSecureInput = Read-Host 'Mat khau Admin local' -AsSecureString
$adminCredential = [System.Management.Automation.PSCredential]::new($env:ELP_ADMIN_EMAIL, $adminSecureInput)
$env:ELP_ADMIN_PASSWORD = $adminCredential.GetNetworkCredential().Password
try {
    node tests/browser/admin-dashboard-uat.mjs
} finally {
    Remove-Item Env:\ELP_ADMIN_PASSWORD -ErrorAction SilentlyContinue
    Remove-Variable adminCredential, adminSecureInput -ErrorAction SilentlyContinue
}
```

Không ghi mật khẩu vào lệnh, source, ảnh hay tài liệu. Script chỉ chấp nhận URL local và ghi báo cáo/ảnh vào `TestResults/admin-dashboard-<thời điểm UTC>/` đang được Git bỏ qua. Nếu lưu bằng chứng để gửi leader, kiểm tra thông tin cá nhân trong ảnh trước khi chia sẻ.

Browser UAT ngày 24/10/2026: **19 kiểm tra đạt**, 12 ảnh chụp (ba trang × bốn kích thước màn hình), không có lỗi JavaScript. Lượt chạy `TestResults/admin-dashboard-2026-10-24T10-21-18-780Z/` kiểm tra dữ liệu local hiện tại; số liệu hỗn hợp/phân trang 21 dòng được kiểm chứng riêng bằng integration test. Các ảnh nằm trong thư mục kết quả local, không tự đưa vào Git.

## Phạm vi và Git

- Không thêm migration, không thay schema hay seed dữ liệu vào database ứng dụng. Dữ liệu test nằm trong database riêng của fixture.
- Các count là truy vấn riêng; đây là tổng quan vận hành, không phải snapshot kế toán nguyên tử khi dữ liệu đang thay đổi.
- Chưa có doanh thu hoặc lọc thời gian thống kê: thuộc Task 03. Các thanh phân bố trạng thái/tỷ lệ hoàn thành ở đây chỉ dùng số liệu Task 02.
- Nhánh này được tách từ nhánh Task 01 hiện có. Nếu PR #62 chưa merge, dùng base PR là `feature/duong-admin-date-filters` để review riêng Task 02; sau khi merge Task 01 thì cập nhật base về main và kiểm tra diff.
- Các thay đổi chưa commit của Task 01 (`AdminDateFilterTests.cs`, `docs/acceptance/duong-admin-task01.md`) vẫn giữ trên máy; không tự đưa vào commit Task 02.
- Chưa commit/push; Dương tự thực hiện và leader review trước khi merge.
