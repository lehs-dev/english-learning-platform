# Course công khai và Teacher authoring

## Chức năng đã triển khai

- `/Courses` (và `/`): chỉ khóa Published; tìm theo tên, lọc Level/Skill/Topic/Teacher/Free-Paid, 9 khóa/trang, sắp xếp Title rồi Id. Pagination giữ bộ lọc, page ngoài giới hạn được chuẩn hóa; query lỗi có thông báo, kết quả rỗng có hướng dẫn.
- `/Courses/Details/{id}`: metadata, mục tiêu, Teacher, giá VND và cấu trúc Module Visible / Lesson Published. Không truy vấn hay trả ContentText/ResourceUrl. Draft/Unpublished/Archived trả 404 tại route công khai.
- `/TeacherCourses`: khóa của Teacher hiện tại, tạo Draft, sửa metadata/giá và navigation authoring.
- `/TeacherCourses/Manage/{id}`: cây Module → Lesson → resource; thêm/sửa, đổi thứ tự lên/xuống ở cả ba cấp, xóa resource, preview, Publish/Unpublish/Archive. Không hard-delete Module/Lesson; dùng Hidden. Khóa có Enrollment hiện cảnh báo ảnh hưởng khi đổi cấu trúc.
- `/Learning`: khóa đã tham gia, bao gồm Unpublished/Archived; `/Learning/Course/{id}`, `/Learning/Lesson/{id}`, `/Learning/Resource/{id}` yêu cầu quyền trước khi đọc nội dung. Module route chuyển về khu học tập sau kiểm tra quyền. Route ManageCourse cũ vẫn hoạt động.
- Free vẫn cần enroll bằng POST có CSRF. Chỉ Student Active, khóa Published, giá 0 và owner không Disabled. ID Student lấy từ claims, role/status đọc lại server. Unique index và transaction với khóa range chống enroll trùng, kể cả các request đồng thời.

## Quy tắc và giới hạn

Owner Teacher Active được preview Draft/Hidden; Teacher khác và Admin không được quản lý/đọc protected content. Student cần Enrollment đúng Course, Lesson Published và Module Visible. CompletedAt không thu hồi quyền đọc. Unpublished không public/enroll mới nhưng giữ quyền cũ; Archived giữ quyền đọc, `RecordProgress` bị chặn. Thứ tự không phải điều kiện tiên quyết.

Course metadata form không bind owner/status/FinalAssessment; tạo mới luôn Draft. Server trim tên và kiểm tra độ dài, enum, parent và ownership. Free giá 0; Paid giá > 0, trong decimal(18,2), tối đa 2 chữ số thập phân. Lesson Published cần resource hợp lệ; không xóa resource cuối khi Lesson còn Published. Resource Text hiển thị bằng Razor encoding, không hỗ trợ HTML/rich text; URL chỉ http/https. Audio/Video dùng URL file media trực tiếp, Link có thể dẫn tới trang ngoài; không tự nhúng iframe.

Publish là POST riêng: kiểm tra metadata/giá, mọi Lesson Published và ít nhất một Module Visible chứa Lesson Published có resource hợp lệ. Nếu có FinalAssessment, kiểm tra đúng Course/owner, Published, loại SkillAssessment/PracticeExam và PassingScore 0–100. Final không bắt buộc và đợt này không thêm giao diện gắn Final. Ẩn nội dung không tự Unpublish; không còn bài khả dụng thì hiện “Chưa có nội dung khả dụng”.

Reorder phải gửi đủ ID không trùng của cùng parent. Transaction ghi các index tạm thấp hơn mọi index hiện tại, rồi ghi index cuối; tránh va chạm unique index và giữ ID/progress. POST lỗi trả thông báo hoặc field validation. Trang protected và detail có dữ liệu theo user dùng `no-store`.

**Paid đã có Order/checkout và webhook phía server.** Nút mua tạo/reuse Order và chuyển tới trang đơn riêng của Student; liên kết checkout chỉ xuất hiện khi gateway ngoài được cấu hình. Chỉ webhook ký đã xác minh mới ghi Payment và cấp Enrollment. Xem [hợp đồng và cấu hình payment](../architecture/payment-sandbox.md). Enrollment có Payment phải khớp Payment Succeeded, Student/Course của Order, currency VND và Amount của Order lúc mua. Không so với giá Course hiện tại. Enrollment free đã cấp với PaymentId null vẫn hợp lệ sau khi đổi Free thành Paid.

## Progress và khu học tập (08/10/2026)

`/Learning` hiển thị progress và liên kết bài học khả dụng. `/Learning/Module/{id}` có trang Module riêng; Lesson có breadcrumb, bài trước/sau theo thứ tự Module/Lesson và trạng thái hoàn thành. Giáo viên sở hữu khóa preview được Draft/Hidden; Student chỉ thấy Module Visible và Lesson Published.

POST `/Learning/Completion/{lessonId}` nhận `completed=true/false`, có CSRF, chỉ Student Active có Enrollment hợp lệ được ghi. Server đặt trạng thái yêu cầu thay vì đảo trạng thái, nên POST lặp cùng trạng thái là idempotent. Transaction khóa Course trước khi kiểm tra quyền/lifecycle và ghi LessonProgress; unique (EnrollmentId, LessonId) chống trùng. Vị trí tiếp tục học được cập nhật khi Student đổi completion; nếu vị trí cũ không còn khả dụng, chọn bài chưa hoàn thành đầu tiên, rồi bài khả dụng đầu tiên. Mở trang GET không tự đánh dấu hoàn thành hoặc thay đổi progress.

Progress tính live: completed Lesson Published trong Module Visible / tổng Lesson Published trong Module Visible. Tổng bằng 0 trả Percentage null, hiện “Chưa có nội dung khả dụng”, không tính 0%/100%, không có liên kết tiếp tục hoặc thao tác completion trên bài bị ẩn. Giữ các bản ghi tiến độ cũ để khi hiện lại nội dung có thể tính lại.

Unpublished giữ quyền học và completion của Student đã enroll. Archived chỉ đọc: xem content/progress/historical completion, không POST completion. Owner Disabled chặn enrollment mới nhưng giữ quyền học cũ.

Khi completion hoặc tập bài khả dụng thay đổi, kiểm tra CompletedAt trong cùng transaction: tập bài phải không rỗng và hoàn thành hết. Nếu có Final, yêu cầu Final cùng Course/owner, Published, SkillAssessment/PracticeExam và có Attempt Graded đạt PassingScore. CompletedAt chỉ ghi lần đầu, không xóa khi đánh dấu Incomplete, thêm/ẩn bài hoặc progress giảm. Luồng chấm/gắn/thay Final chưa triển khai trong repository; khi bổ sung phải nối bước xét completion ngay sau mutation Final theo BR14.

Ứng dụng bảo vệ việc tiết lộ URL resource ngoài. Sau khi người có quyền nhận URL công khai, ứng dụng không thể ngăn họ chia sẻ URL đó. Không lưu file protected trong wwwroot.

## Chạy local và database demo

Không có migration mới trong thay đổi này. Dùng các migration hiện có, gồm `AddCommerce` và `MakeEnrollmentPaymentIdNullable`. SQL Server Windows Authentication:

Dừng ứng dụng đang chạy bằng Ctrl+C/Stop Debugging trước khi build hoặc chạy EF trên Windows để tránh DLL bị khóa (MSB3021/MSB3027). Chạy web sau khi cập nhật database xong.

```powershell
$env:ConnectionStrings__DefaultConnection = 'Server=localhost;Database=EnglishLearningDemoDb;Integrated Security=True;TrustServerCertificate=True'
dotnet ef database update --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web
dotnet run --project src/EnglishLearningPlatform.Web --launch-profile http --urls http://localhost:5098
```

Nếu muốn dùng database ứng dụng sẵn có, chạy profile `LocalSqlServer` theo README. Profile này ghi đè connection string về EnglishLearningDb; **không dùng profile đó nếu muốn database demo riêng**.

`dotnet ef` không đọc launch profile. Với database ứng dụng Windows local, có thể cập nhật trực tiếp bằng `dotnet ef database update --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web --connection 'Server=localhost;Database=EnglishLearningDb;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=true'`, rồi chạy `dotnet run --project src/EnglishLearningPlatform.Web --launch-profile LocalSqlServer`. Nếu không đặt connection cho EF, cấu hình mặc định `localhost,1433` dành cho Docker có thể gây TCP timeout.

Seed opt-in chỉ được gọi trong Development. Cấu hình mật khẩu local đủ yêu cầu Identity (ít nhất 8 ký tự, chữ hoa/thường, số, ký tự đặc biệt). Nhập qua prompt để không ghi mật khẩu vào repo/lịch sử lệnh:

```powershell
$demoCredential = Read-Host 'Mật khẩu demo local' -AsSecureString
$env:LearningDemo__Password = [System.Net.NetworkCredential]::new('', $demoCredential).Password
$env:LearningDemo__Enabled = 'true'
dotnet run --project src/EnglishLearningPlatform.Web --launch-profile http --urls http://localhost:5098
Remove-Item Env:LearningDemo__Password, Env:LearningDemo__Enabled
```

Giữ connection string demo ở cùng terminal, áp dụng migration trước seed. Không bật seed trong production. Khi chạy lại, seed sử dụng tài khoản/khóa đã tạo, không reset mật khẩu hoặc role và không tạo Enrollment trùng. Nếu đã có tài khoản demo, dùng mật khẩu đã cấu hình lần đầu. Seed không cấp Enrollment Paid.

Tài khoản: `teacher1@demo.example.test`, `teacher2@demo.example.test`, `student1@demo.example.test`, `student2@demo.example.test`. Cả bốn dùng mật khẩu local đã nhập. Teacher1 có Free Published với Text/Link/Audio/Video, Paid Published và Draft trống. Có Module Hidden, Lesson Draft/Hidden. Student1 đã enroll Free; Student2 chưa enroll. Media mẫu cần Internet và phụ thuộc máy chủ ngoài.

## Test tay

1. Teacher1 vào `/TeacherCourses` → tạo Draft → sửa metadata/Free-Paid, kiểm tra lỗi giá và tên trống.
2. Thêm Module Visible → Lesson Draft → thêm Text/Link/Audio/Video → sửa Lesson thành Published. Bấm lên/xuống; preview Draft/Hidden; thử Publish khi chưa đủ nội dung để xem lý do.
3. Publish khóa đủ điều kiện. Guest vào `/Courses`, tìm/lọc, xem detail: chỉ thấy cấu trúc; mở trực tiếp Lesson/resource phải đăng nhập.
4. Student2 đăng nhập, xem detail khóa Free → tham gia → mở Lesson bất kỳ. Thử request enroll lặp; thử Lesson của khóa Paid/chưa enroll phải bị chặn.
5. Teacher2 truy cập đường dẫn Manage/Content/Lesson/resource của Teacher1: bị từ chối. POST thiếu CSRF token trả 400.
6. Teacher1 đổi giá/loại khóa, Unpublish/Archive. Student đã enroll vẫn đọc từ `/Learning`; public detail trả 404. Archived không được ghi progress qua access service.
7. Ẩn mọi Module/Lesson khả dụng: khóa vẫn Published nhưng người học thấy “Chưa có nội dung khả dụng”. Kiểm tra form/điều hướng ở desktop và mobile.

## Kiểm thử tự động

```powershell
$env:ELP_TEST_SQLSERVER = 'Server=localhost;Integrated Security=True;TrustServerCertificate=True'
dotnet build EnglishLearningPlatform.sln
dotnet test EnglishLearningPlatform.sln
git diff --check
Remove-Item Env:ELP_TEST_SQLSERVER
```

Fixture luôn tạo/xóa database riêng `EnglishLearningPlatformTests_<GUID>`, không sử dụng database ứng dụng. Bỏ biến ELP_TEST_SQLSERVER nếu muốn chạy SQL Server Testcontainers (cần Docker).

Các file chính: Domain `CourseRules`; Application `CourseModels`/`ICourseService`; Infrastructure `CourseService`, `LearningAccessService`, `LearningDemoSeeder`; Web controllers `Courses`, `TeacherCourses`, `Learning`, Razor views và `courses.css`/`course-editor.js`. `CourseFeatureTests` kiểm tra catalog/filter/pagination, không rò rỉ nội dung, quyền nested resource, validation, CSRF, flow authoring/enroll qua form HTTP, reorder trên unique index có progress, enroll đồng thời và giữ quyền học cũ khi đổi giá/lifecycle. `CourseRulesTests` kiểm tra FinalAssessment và URL.

Kết quả ngày 03/10/2026: build thành công với 0 warning/0 error; 32 unit tests và 38 integration tests pass trên SQL Server local; `git diff --check` pass. Seed Development đã chạy trên database kiểm thử riêng `EnglishLearningCourseDemo`; ứng dụng kiểm thử đã dừng. Database demo theo lệnh hướng dẫn ở trên là `EnglishLearningDemoDb`, để bạn tự chọn credential từ lần seed đầu tiên.

Kết quả 03/10 ở trên là lịch sử của đợt authoring. Đợt 08/10 bổ sung checkout/webhook, ghi progress và Module navigation; có HTTP integration tests kiểm tra Razor/form/quyền và các request đồng thời. Chưa chạy thử với cổng thanh toán thật hoặc xác minh trực quan desktop/mobile. Upload file protected chưa thuộc phạm vi.
