# Chương: Phân hệ Learning và Commerce

## 1. Mục tiêu và phạm vi

Phân hệ Learning tổ chức khóa học theo cấu trúc Course → Module → Lesson → Resource, cấp quyền đọc nội dung cho học viên đã ghi danh và theo dõi tiến độ. Phân hệ Commerce hỗ trợ ghi danh khóa miễn phí và quy trình Order → Payment đã xác minh → Enrollment đối với khóa trả phí. Hai phân hệ phối hợp để việc mua khóa và việc học sử dụng cùng một quyền ghi danh được kiểm tra phía server.

Người dùng gồm Guest, Student, Teacher và Admin. Guest xem thông tin công khai; Student tham gia khóa và học; Teacher quản lý nội dung và báo cáo của khóa mình sở hữu; Admin có trang tra cứu tài khoản/giao dịch. Guest là người chưa đăng nhập, không phải role trong database. Mỗi tài khoản có một role hoạt động và phải có trạng thái Active để sử dụng chức năng được bảo vệ.

Đợt Tuần 6 tập trung kiểm thử liên module và UAT. Các tính năng Learning/Commerce đã có được giữ nguyên; mã bổ sung là test, chương trình chạy kiểm thử, kịch bản UAT và tài liệu. Cổng thanh toán bên ngoài, thiết kế lại giao diện, quản lý ngân hàng câu hỏi và chỉnh sửa phân hệ Assessment được nghiệm thu theo phạm vi riêng.

## 2. Kiến trúc và dữ liệu

Ứng dụng là modular monolith dùng ASP.NET Core MVC và bốn project. Domain định nghĩa entity/enum/quy tắc; Application chứa hợp đồng và DTO; Infrastructure triển khai EF Core, SQL Server, Identity và service; Web chứa controller, Razor view và tài nguyên giao diện. Razor hiển thị dữ liệu do service cung cấp; controller lấy actor từ claims và kiểm tra CSRF trước các thao tác form.

Các đối tượng dữ liệu tham gia luồng:

| Đối tượng | Dữ liệu/trách nhiệm |
| --- | --- |
| Course | OwnerTeacherUserId, trạng thái, cấp độ, Free/Paid và giá; có nhiều Module. |
| Module | Thuộc Course, thứ tự, Visible/Hidden; có nhiều Lesson. |
| Lesson | Thuộc Module, thứ tự, Draft/Published/Hidden; có Resource. |
| LessonResource | Text hoặc URL Link/Audio/Video; thuộc Lesson. |
| Enrollment | StudentUserId, CourseId, PaymentId tùy chọn, ngày tham gia, vị trí tiếp tục và CompletedAtUtc. |
| LessonProgress | EnrollmentId, LessonId, IsCompleted và thời điểm cập nhật. |
| Order | Student/Course, giá VND được chốt lúc tạo, Provider và thời hạn. |
| Payment | Order, số tiền, trạng thái, ProviderTransactionId và PaidAtUtc. |
| AuditLog | Ghi nhận xác minh thanh toán và điều kiện cấp Enrollment. |

AppDbContext kế thừa IdentityDbContext và áp dụng các EF configuration. Unique `(StudentUserId, CourseId)` ngăn Enrollment trùng; unique `(EnrollmentId, LessonId)` giữ một trạng thái tiến độ cho mỗi bài; unique `Payment.ProviderTransactionId` ngăn ghi hai receipt cho cùng giao dịch. Tên bảng thương mại thực tế là `[Order]` và `Payment`; không suy từ tên DbSet `Orders`/`Payments` để viết SQL.

Migration cập nhật schema; seeder tạo dữ liệu demo sau migration. Bộ gate Tuần 6 dùng SQL Server 2022 container mới, database GUID cho từng test fixture và database demo GUID riêng. Chạy kiểm thử không reset database ứng dụng của nhóm.

## 3. Khóa học, nội dung và quyền sở hữu

Course có Draft, Published, Unpublished và Archived. Chỉ Published xuất hiện trong catalog và cho ghi danh mới. Unpublished giữ quyền học và ghi tiến độ của người đã ghi danh. Archived giữ quyền đọc nội dung khả dụng nhưng chặn ghi tiến độ. Draft không mở nội dung cho Student.

Một Lesson hiệu lực phải Published trong Module Visible. Hidden/Draft Lesson và Lesson trong Module Hidden không xuất hiện trong cấu trúc Student, không được mở bằng cách đoán URL và không được ghi tiến độ. Owner Teacher vẫn preview được nội dung của mình. Teacher khác không được đọc protected resource, mở report hoặc thay đổi khóa; mọi kiểm tra thực hiện ở service/controller phía server.

Catalog/detail công khai trả metadata, giá và cấu trúc hiệu lực, không trả ContentText/ResourceUrl. Text được Razor encode; URL resource phải hợp lệ theo http/https. Học viên có thể mở bài theo bất kỳ thứ tự, không có điều kiện phải hoàn thành bài trước.

Các form authoring hỗ trợ metadata, Module/Lesson/resource, thứ tự và Publish/Unpublish/Archive. Publish kiểm tra tên/cấp độ/giá, resource hợp lệ và ít nhất một Module Visible có Lesson Published. Nếu khóa gắn Final, định nghĩa Final phải đúng Course/owner, loại, trạng thái và ngưỡng đạt.

## 4. Ghi danh Free và thanh toán Paid

### 4.1. Khóa miễn phí

Student gửi POST `/Courses/EnrollFree/{courseId}` có CSRF. Server đọc StudentId từ principal, kiểm tra role/status, Course Published, Free/giá 0 và owner khả dụng. Trong transaction Serializable, khóa Course và range Enrollment trước khi tạo. Request lặp trả về cùng quyền học và không tạo Order/Payment.

Enrollment không qua Payment vẫn hợp lệ nếu khóa được đổi từ Free sang Paid về sau. Điều này giữ quyền đã cấp thay vì buộc học viên mua lại vì metadata thay đổi.

### 4.2. Khóa trả phí

```mermaid
sequenceDiagram
    actor S as Student
    participant W as Web/CheckoutService
    participant G as Gateway hoặc Sandbox
    participant D as SQL Server
    S->>W: POST tạo Order + CSRF
    W->>D: Kiểm tra Course/actor; chốt giá và lưu Order
    W-->>S: Trang đơn chờ thanh toán
    S->>G: Thanh toán hoặc chọn tình huống demo
    G->>W: Receipt có chữ ký
    W->>W: Xác minh chữ ký, Order, provider, giá và thời gian
    W->>D: Transaction: Payment + Enrollment khi đủ điều kiện
    W-->>S: Trạng thái đã lưu
    S->>W: Mở Lesson
    W->>D: Kiểm tra Enrollment/Payment hợp lệ
    W-->>S: Nội dung được phép đọc
```

POST tạo Order lấy giá và buyer từ server; browser không tự chọn giá/StudentId. Order chưa có Payment, còn hạn và cùng quote/provider có thể được tái sử dụng. GET trang kết quả hoặc query `paid=true/status=Succeeded` chỉ đọc trạng thái, không cấp quyền.

Hosted adapter xác minh HMAC-SHA256 trên đúng raw bytes body. Local sandbox chỉ bật opt-in Development, dùng khóa ngẫu nhiên trong process để ký envelope và không thu tiền thật. Hai adapter kiểm tra provider của Order và không xác nhận đơn của nhau. Đồ án chưa tích hợp protocol VNPay/MoMo hoặc chứng minh giao dịch provider ngoài.

Receipt Failed không cấp Enrollment. Local Failed/Cancelled là terminal; mua lại tạo đơn mới. Callback cùng receipt được xử lý lặp mà không tạo thêm Payment/Enrollment; receipt xung đột bị từ chối. Payment Succeeded chỉ cấp quyền mới nếu Course vẫn Published/Paid, buyer Active/Student và owner còn khả dụng. Receipt hợp lệ tới sau Unpublish/Archive có thể được lưu để giữ lịch sử, nhưng không mở quyền học mới khi khóa còn ở trạng thái đó.

Enrollment Paid hợp lệ cần Payment Succeeded, đúng Student/Course của Order, số tiền Payment bằng quote Order và currency VND. Giá Course thay đổi không viết lại quote lịch sử. Callback replay không được làm mất tiến độ hoặc tăng tổng tiền báo cáo.

## 5. Tiến độ hiện tại và lịch sử hoàn thành

Với `N` là số Lesson hiệu lực và `C` là số Lesson hiệu lực đã hoàn thành, tiến độ hiển thị là `round(C × 100 / N, 2)`. Khi `N = 0`, phần trăm là null và giao diện hiện “Chưa có nội dung khả dụng”, không tính 0/0 hoặc tự nhận đã hoàn thành.

POST `/Learning/Completion/{lessonId}` nhận trạng thái rõ ràng Completed/Incomplete, không đảo trạng thái mỗi lần POST. Server kiểm tra quyền và Course lifecycle, cập nhật một LessonProgress và reconcile completion trong cùng transaction. LastAccessedLessonId được cập nhật cùng thao tác này; GET Continue không ghi tiến độ.

Progress thay đổi theo nội dung đang hiệu lực. Ẩn một Lesson loại bài khỏi mẫu số và tử số, nhưng giữ row progress. Khôi phục Lesson đưa trạng thái cũ trở lại. Publish thêm một bài có thể làm tiến độ giảm từ 100% xuống 66,67%. Ẩn hết Module đưa progress về trạng thái không có nội dung. Continue kiểm tra lại bài khả dụng và chọn fallback khi vị trí cũ bị ẩn.

CompletedAtUtc là mốc lịch sử lần đầu, độc lập với progress hiện tại. Mốc này không bị xóa khi thêm/ẩn bài, đổi Incomplete hoặc đổi lifecycle. Khóa không có Final được xét hoàn thành khi tập bài hiệu lực không rỗng và tất cả đã hoàn thành. Khóa có Final chỉ hoàn thành khi còn Enrollment hợp lệ, đủ bài và đã có Final Graded đạt ngưỡng theo điểm thô. Phân hệ Assessment chịu trách nhiệm grade; Learning nhận kết quả để reconcile completion.

## 6. Tính nhất quán và bảo mật

Identity kiểm tra tài khoản/role; authorization kiểm tra ownership và enrollment ở server. Browser POST có CSRF, protected pages có no-store, webhook server-to-server có chữ ký và giới hạn kích thước. Dữ liệu người học khác không được truy cập bằng Order/resource/report ID.

Serializable transaction và thứ tự khóa nhất quán giảm race giữa callback/enrollment/progress. Unique constraint là hàng rào cuối. Grade/completion trong luồng Final và Payment/Enrollment trong luồng thương mại commit nguyên tử. Checkout transaction executor giữ lỗi chính khi rollback/dispose gặp lỗi và ghi log metadata thay vì credential/body/chữ ký.

Teacher report lọc CourseId và owner trước aggregate/pagination. Báo cáo phân biệt Enrollment Free/Paid theo liên kết Payment, số người từng hoàn thành và progress hiện tại. Tổng tiền dùng receipt Succeeded đúng quote VND của Order; không cộng Failed/Pending/Cancelled. Bộ lọc danh sách Student không đổi aggregate toàn khóa.

## 7. Kiểm thử, UAT và minh chứng

Đợt Tuần 6 bổ sung `LearningCommerceJourneyTests` gồm 15 case chạy qua HTTP/form/callback và đối chiếu dữ liệu lưu. Các suite LearningAccess, CourseFeature, EnrollmentProgress, CheckoutFeature/Flow/Merge/Failure, LearningDashboard và CourseCompletion hiện có tiếp tục nằm trong regression toàn solution.

UAT tự động dùng Chromium với context riêng cho Guest/Student/Teacher/Admin. Suite hiện có kiểm tra 6 hành trình, gồm seeded Course/Payment/Learning/Final; suite Learning/Commerce mới kiểm tra 10 hành trình, gồm authoring dữ liệu qua UI, public privacy, owner khác, Free duplicate, Pending/Failed/Success/replay, học không tuần tự, progress động, Unpublish/Archive và report Teacher. Có ảnh desktop và mobile; SQL chỉ đọc đối chiếu Order/Payment/Enrollment/progress sau UAT.

Kết quả, SHA nguồn, source digest, test count, link CI và ảnh của lần chạy thật được ghi tại [biên bản Tuần 6](../acceptance/learning-commerce-week6.md). Số case focus nằm trong tổng regression và không được cộng hai lần. Đây là UAT automation, không thay chữ ký nghiệm thu/tổng duyệt của thành viên.

## 8. Vận hành và tái hiện

Trên Windows cần .NET 10, Docker Desktop dùng Linux containers, Node/npm và Chromium Playwright. Trong root repo:

```powershell
Push-Location tests/browser
npm.cmd ci
npx.cmd playwright install chromium
Pop-Location
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/learning-commerce-gate.ps1
```

Gate restore/build, khởi động SQL Server 2022 riêng, chạy test focus/unit/integration, migrate database demo, seed bằng mật khẩu ngẫu nhiên chỉ trong environment, mở web và chạy hai suite browser. Report/screenshot/log/TRX nằm trong `artifacts/learning-commerce-week6/final`. Kết thúc gate dừng đúng web process và xóa đúng container do gate tạo; dữ liệu máy của nhóm không nằm trong container này.

Ảnh demo chỉ chứa tài khoản giả. Không đóng gói password, cookie, connection string hoặc chữ ký HMAC. Nếu thiết kế lại FE sau đợt này, chạy lại UAT và thay ảnh minh chứng theo UI mới; các test backend tiếp tục kiểm tra hợp đồng nghiệp vụ.
