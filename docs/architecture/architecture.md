# Kiến trúc hệ thống

**Đề tài:** Nền tảng học tập và đánh giá năng lực tiếng Anh trực tuyến trên web. Một mã nguồn phục vụ Công nghệ .NET (sản phẩm), Các công nghệ lập trình hiện đại (minh chứng kỹ thuật) và Quản lý dự án phần mềm (phạm vi, tiến độ, nghiệm thu). [Phạm vi đã đề xuất](../requirements/scope.md) ghi quyết định cần nhóm xác nhận; [README](../../README.md) mô tả hành trình người dùng; [ERD](erd.md) mô tả mô hình **đích**. Các module bên dưới chưa phải đều đã được triển khai trên `main`.

## Cấu trúc solution và phụ thuộc

| Project/thư mục | Trách nhiệm | Được phụ thuộc vào |
| --- | --- | --- |
| `Domain` | Entity, enum, invariant nghiệp vụ không phụ thuộc web/database. | Không phụ thuộc project khác. |
| `Application` | Use case/service, DTO, interface cần cho persistence/clock/current user. | `Domain`. |
| `Infrastructure` | EF Core/SQL Server, Identity, triển khai interface của Application, migration. | `Application`, `Domain`. |
| `Web` | MVC controller/view, cấu hình middleware/DI, chính sách auth, endpoint. | `Application`, `Infrastructure`. |
| `tests` | Unit test quy tắc và integration test luồng HTTP/database khi được thêm. | Các project cần kiểm thử. |

Giữ **modular monolith** bốn project hiện có; tổ chức code theo Learning, Practice, Assessment, Identity, Admin trong từng project khi số use case tăng. Không cần tạo microservice hoặc một project riêng cho mỗi bảng. Controller nhận/validate request, gọi Application, trả response; không đặt quy tắc chấm điểm, ownership và transaction xuyên bảng trong view/controller. `Infrastructure` hiện đăng ký `AppDbContext`/Identity; `Application` đăng ký scoring service. Không đưa EF/Identity vào `Domain`.

## Ranh giới nghiệp vụ

| Module | Sở hữu quy tắc chính | Hợp đồng với module khác |
| --- | --- | --- |
| Identity/Admin | Guest đăng ký Student; đúng một role đang hoạt động; login/logout, Locked/Disabled; Admin quản lý user/audit nhưng không sửa nội dung Teacher. | Cấp current user/role; mọi endpoint còn kiểm tra resource ownership ở server. |
| Learning | Course → Module → Lesson, resource, Enrollment, Completed/Incomplete, progress và CompletedAt. | Course owner và effective published status để Practice/Assessment kiểm tra; nhận điểm Final để xét completion. |
| Question Bank | Question MCQ, Option, PrimarySkill, Stimulus, quyền owner, vòng đời Publish/Archive. | Practice/Assessment dùng Question của cùng Teacher; giữ định nghĩa câu hỏi sau lần Publish. |
| Practice | Bài luyện gắn Lesson hoặc Module, làm lại tùy ý, chấm đều, chỉ giữ kết quả gần nhất, feedback chi tiết. | Kiểm tra parent cùng Course/owner và quyền Student đã enroll. |
| Assessment | Placement/Skill/Mock Exam, đề và points, Attempt, deadline server, scoring/history/skill breakdown. | Assessment độc lập hoặc link Course; Final hợp lệ tác động Course completion; recommendation dùng skill đủ dữ liệu. |

Trong MVP câu hỏi là single-choice MCQ. Reading có passage, Listening cần audio stimulus; `PrimarySkill` là Reading/Listening/Vocabulary/Grammar. Placement không tự suy ra chứng nhận CEFR/TOEIC. Phần ngoài phạm vi hiện tại: thanh toán, livestream, forum và chấm nói/viết bằng AI.

## Luồng HTTP và bảo mật

`Program.cs` hiện đăng ký MVC, Application/Infrastructure, health check; pipeline là xử lý lỗi ngoài Development → HTTPS redirect → static files → routing → authentication → authorization → endpoint. Giữ AuthN trước AuthZ. Dùng `DbContext` scoped theo request qua DI; service nghiệp vụ dùng lifetime phù hợp dependency scoped. `Identity` hash mật khẩu và quản lý cookie; không tự tạo bảng/mật khẩu riêng.

Kiểm tra role/owner trong server: Guest chỉ thấy nội dung công khai; Teacher chỉ sửa Course/Question/Assessment của mình; Student chỉ học Course đã enroll và làm Assessment khả dụng; Admin quản trị user, không có quyền mặc định sửa bài Teacher. Trả 401 khi chưa xác thực và 403 khi đã xác thực nhưng không có quyền. UI ẩn nút không thay cho authorization. Các thao tác Start/Submit/finalize và cập nhật Practice gần nhất phải an toàn khi request lặp hoặc đồng thời.

## Hợp đồng dữ liệu trước migration

1. Sơn, Khánh, Hoàng đối chiếu đặc tả và [ERD đích](erd.md), chốt `Course → Module → Lesson`, Practice riêng với Assessment, Assessment độc lập/Final, trạng thái Publish/Archive và ràng buộc lịch sử.
2. Thống nhất EF mapping, unique index, FK/delete behavior và transaction; chọn **một owner** tạo migration đầu tiên. `AppDbContext` hiện có các FK cascade có thể xóa Enrollment/Attempt theo Course/Exam; không tạo `InitialCreate` dựa trên model cũ.
3. Người khác review migration và thử fresh database/update. Seed dữ liệu và tài khoản demo không chứa secret thật. Khi thay đổi contract, cập nhật ERD, code, test và ghi trong PR.

## Kiểm thử, CI và mốc bàn giao

- Unit test: scoring, progress và business rule; integration test: auth/owner, EF constraints, các request Start/Submit/timeout, luồng bốn actor. Kiểm tra ranh giới điểm trước làm tròn, kết quả không nhân đôi và CompletedAt lịch sử.
- `.github/workflows/ci.yml` hiện chạy restore/build/**unit tests** trên PR/push vào `main`; integration test phải chạy thêm theo issue/PR liên quan cho đến khi nhóm bổ sung vào CI. CI xanh của skeleton không chứng minh toàn bộ chức năng đã xong.
- `.github/workflows/cd.yml` build/push container lên GHCR trên `main`/tag `v*`; chưa có deploy tới môi trường dùng thật.
- Tuần 7: demo xuyên suốt đăng ký → học → làm bài → chấm → kết quả/progress, giải thích được middleware, DI, EF, Identity và Git/CI. Tuần 13: hoàn thiện phạm vi .NET và minh chứng quản lý dự án/báo cáo.
