# SP14 · Hồ sơ kiểm thử và nghiệm thu

Ứng viên phát hành cho Công việc 10, 11 và 12

Ngày lập: 11/10/2026 (Asia/Saigon). Chủ trì: Lê Hoàng Sơn — 3123560077. Mọi trạng thái Pass phải dẫn tới kết quả thật của đúng candidate. Hồ sơ này là matrix và biên bản có thể điền; các mục chưa thực hiện giữ Pending. Nhật ký chi tiết do người chốt release cập nhật tại docs/acceptance/evidence/son-sprint-16-10-evidence.md.

## 1. Thông tin candidate

| Trường | Giá trị cần ghi khi chốt |
| --- | --- |
| Branch | Lấy từ git branch --show-current của lượt kiểm thử. |
| Candidate SHA / tag | Lấy từ git rev-parse HEAD; tag RC không thay cho green gate. |
| Thời gian và múi giờ | Ghi bắt đầu/kết thúc thực tế, Asia/Saigon. |
| Môi trường | SDK, OS, SQL Server, Docker/image và URL demo; che secret. |
| Người chạy / người nghiệm thu | Ghi tên thật khi có; automation ghi rõ automation. |
| Remote CI run | Pending cho tới có run URL, SHA và artifact thật. |

Không gán số “331 tests” trong kế hoạch thành tổng test candidate. Đọc Counters trong TRX của lần chạy cuối; báo tổng/pass/fail/skip theo từng suite. Log lịch sử ở nhánh trước chỉ là baseline và không dùng để nghiệm thu mã mới.

## 2. Điều kiện và bộ bằng chứng

Release kỹ thuật cần Release build thành công, unit/integration không fail/skip ngoài lý do đã được phê duyệt, security audit không blocker, fresh database migrate/seed chạy được và Browser UAT trên candidate. Release phục vụ bảo vệ còn cần hai rehearsal người thật, hồ sơ bàn giao và cập nhật tiến độ đúng quyền.

| Bằng chứng | Nơi ghi / yêu cầu |
| --- | --- |
| Restore/build/test | Thư mục release gate có build.log, unit.trx, integration.trx, SHA và SDK. |
| Security audit | Checklist endpoint/secret scan có phạm vi branch/history và lỗi đã sửa. |
| Fresh setup | Migration log, health, seeder idempotency và database riêng. |
| Browser UAT | Ảnh Guest/Student/Teacher/Admin, browser và bước kiểm tra; phân biệt automation/manual. |
| Remote CI | Run URL/checkout SHA, test-results artifact đã tải và kiểm tra. |
| Rehearsal | Biên bản mỗi lượt: giờ thật, người thật, candidate, lỗi, quyết định và ảnh/video. |
| Hồ sơ/tiến độ | SP11–SP15, báo cáo/deck, tag, spreadsheet cells thay đổi và đối chiếu. |

Không đưa password, HMAC signature, connection string, cookie hoặc dump dữ liệu cá nhân vào artifact. Kiểm tra nội dung TRX/log trước đóng gói; che console không đảm bảo mọi tệp trực tiếp ghi ra đều được che.

## 3. Truy vết tiêu chí Công việc 10

| AC | Kiểm chứng cần có | Trạng thái |
| --- | --- | --- |
| AC-10.1 | Free enroll; hosted HMAC valid/invalid/replay; local sandbox mua khóa; unique Enrollment. | Pending candidate evidence |
| AC-10.2 | Lesson bất kỳ thứ tự; Completed/Incomplete; mẫu số effective Lesson và không tính 0/0. | Pending candidate evidence |
| AC-10.3 | Final overview đúng metadata; enrolled-only; Taking DTO/HTML không có IsCorrect. | Pending candidate evidence |
| AC-10.4 | Chỉ ghi CompletedAt khi đủ effective Lessons và Final graded đạt điểm thô. | Pending candidate evidence |
| AC-10.5 | Thêm bài/đổi incomplete/failed retake không xóa timestamp hoặc grade/history. | Pending candidate evidence |

Regression chủ yếu: PaidEnrollmentAssessmentAccessTests, CourseCompletionIntegrationTests, AssessmentQueryServiceTests và AssessmentAttempt*Tests. Liệt kê tên test thực sự có trong TRX, không cộng các filter chồng lặp thành tổng.

## 4. Truy vết tiêu chí Công việc 11

| AC | Kiểm chứng cần có | Trạng thái |
| --- | --- | --- |
| AC-11.1 | Secret scan tracked files và Git history; runtime secret từ env/user-secrets; log không lộ. | Pending candidate audit |
| AC-11.2 | MVC mutations kiểm tra CSRF; auth cookie HttpOnly/SameSite/Secure HTTPS. | Pending candidate evidence |
| AC-11.3 | Student isolation; Teacher ownership; Admin content denied; stale cookie role/status. | Pending candidate evidence |
| AC-11.4 | Raw-byte HMAC, altered whitespace/signature fail, amount/provider/time validation, replay safe. | Pending candidate evidence |
| AC-11.5 | CI remote chạy Release build/unit/integration, exit 0 và test-results cùng candidate SHA. | Pending remote run |

Webhook không có form antiforgery vì là request server-to-server; ngoại lệ được bảo vệ bằng HMAC raw body, size limit và receipt validation. Không được đánh dấu FAIL chỉ vì không dùng token MVC ở webhook; cũng không được bỏ kiểm tra chữ ký vì có HTTPS.

## 5. Truy vết tiêu chí Công việc 12

| AC | Kiểm chứng cần có | Trạng thái |
| --- | --- | --- |
| AC-12.1 | Database Docker mới, apply migrations, pending-model check và health. | Pending fresh setup |
| AC-12.2 | Đủ 5 tài khoản; seed lại không trùng/không mất dữ liệu. | Pending candidate evidence |
| AC-12.3 | UAT-01 đến UAT-05, Student 1/2 và Teacher/Admin/Guest, không blocker. | Pending browser run |
| AC-12.4 | Hai rehearsal người thật với giờ/SHA/phân vai/bằng chứng. | Pending scheduled sessions |
| AC-12.5 | RC tag đúng SHA và hồ sơ SP11/SP13/SP14/SP15/deck/báo cáo. | Hồ sơ đã soạn; tag/evidence pending |

Có bốn nhóm actor, năm hành trình UAT vì Student tách thành hai tài khoản. Seeder tạo năm user gồm Teacher 2 đối chứng; Guest không được đếm là user.

## 6. Browser UAT chi tiết

Mỗi case ghi: candidate SHA, môi trường, browser/version, người/automation chạy, thời điểm, Actual, trạng thái và đường dẫn ảnh. Dùng cửa sổ/incognito/context riêng cho từng actor để tránh trộn cookie.

### UAT-01 · Guest

Tiền điều kiện: chưa đăng nhập, khóa demo Published. Mở /Courses, xác nhận Everyday English và Business English cùng giá/metadata. Mở chi tiết. Truy cập trực tiếp protected Lesson bằng ID lấy từ chuẩn bị demo.

Expected: xem metadata công khai; protected Lesson dẫn tới Login; không trả nội dung lesson/đáp án. Actual: Pending. Evidence: Pending. Result: Pending.

### UAT-02 · Student 2 thanh toán

Tiền điều kiện: student2@demo.example.test Active, chưa enroll Paid, local sandbox Development bật, hosted tắt. Đăng nhập; mở Paid Business English 199.000 VND; tạo Order; mô phỏng success; xem kết quả; vào /Learning; mở Business introductions; refresh kết quả/khóa.

Expected: Payment Succeeded và Enrollment hợp lệ; học được bài; refresh không tạo duplicate. Kiểm tra DB/test hoặc truy vấn read-only nếu cần chứng minh exact row count. Actual: Pending. Evidence: Pending. Result: Pending.

### UAT-03 · Student 1 thi và completion

Tiền điều kiện: student1@demo.example.test enroll Free, còn lượt Final. Mở Everyday English; hoàn thành mọi bài khả dụng; chụp progress 100% nhưng thiếu Final nếu chưa đạt. Mở Final overview; bắt đầu; chọn đáp án cho ít nhất 4/5 câu đúng theo fixture demo do người chuẩn bị xác định; lưu/nộp; xem điểm/skill; về khóa.

Expected: server chấm đạt >=80% raw, giữ lịch sử; Grammar/Vocabulary breakdown hiển thị; CompletedAt xuất hiện sau đủ cả hai điều kiện. Student HTML không lộ IsCorrect. Actual: Pending. Evidence: Pending. Result: Pending.

### UAT-04 · Teacher 1 và Teacher 2 đối chứng

Đăng nhập teacher1; mở /TeacherCourses; thấy khóa của mình và preview được nội dung. Đăng nhập teacher2 trong context khác; mở ID khóa Teacher 1 trực tiếp và thử mutation bằng form hợp lệ trong môi trường test.

Expected: chỉ quản lý khóa sở hữu; Teacher 2 bị từ chối. Không thay nội dung đã nghiệm thu chỉ để chứng minh quyền; dùng database/fixture riêng khi thử mutation. Actual: Pending. Evidence: Pending. Result: Pending.

### UAT-05 · Admin

Đăng nhập admin1; mở /Admin/Transactions; tìm email student2 hoặc mã đơn; kiểm tra amount/status/date. Mở /Admin/Users; tìm student1 và kiểm tra role/status. Thử mở trang Teacher manage bằng ID trong context Admin.

Expected: Admin đọc giao dịch và user, có pagination/filter; không sửa Course/Question của Teacher, không có đáp án đề. Admin UI read-only. Actual: Pending. Evidence: Pending. Result: Pending.

### UAT-06 · Deadline và submit replay bổ sung

Dùng fixture/deadline riêng thay vì sửa dữ liệu đã có để giả đạt. Bắt đầu attempt, lưu một số đáp án, chờ deadline thật hoặc dùng test clock trong integration. Mở lại/result và nộp replay.

Expected: server chấm đáp án đã lưu, không tiếp nhận đáp án muộn; không đổi điểm/finalized time sau replay. Actual: Pending. Evidence: Pending. Result: Pending.

## 7. Biên bản tổng duyệt lượt 1

Lịch đề xuất theo kế hoạch: 15/10/2026, 09:00–11:30, Asia/Saigon. Đây là lịch tương lai tại thời điểm soạn; chưa phải biên bản đã diễn ra.

| Trường | Nội dung |
| --- | --- |
| Thời gian thực tế / người tham gia | Pending |
| SHA / cấu hình / thiết bị | Pending |
| Nội dung | Setup/migration, seeder, mạng/media, Payment, deadline, authorization, log/SHA. |
| Lỗi và người sửa | Pending; ghi issue, mức ảnh hưởng và test lại. |
| Kết luận / evidence | Pending; không gán hoàn tất khi chưa có người thật chạy. |

## 8. Biên bản tổng duyệt lượt 2

Lịch đề xuất: 15/10/2026, 14:00–16:30, Asia/Saigon. Phân vai dự kiến: Sơn kiến trúc/Identity/HMAC/transaction/completion; Khánh Learning/Teacher; Hoàng Assessment/scoring/history; Khang/Dương bấm demo Student/Admin và giao diện.

| Trường | Nội dung |
| --- | --- |
| Thời gian thực tế / người tham gia | Pending |
| SHA / deck revision | Pending |
| Thời lượng và chuyển vai | Pending; đo thực tế, ghi đoạn cần rút gọn. |
| Kết quả live demo / câu hỏi vấn đáp | Pending |
| Quyết định release / evidence | Pending |

Automation test hoặc walkthrough do agent thực hiện không được ghi thành hai rehearsal có thành viên tham gia. Nếu rehearsal phát hiện đổi source, chạy gate lại trên SHA mới trước chốt tag phát hành.

## 9. Biên bản nghiệm thu và quyết định

Người lập: Lê Hoàng Sơn. Người nghiệm thu/thành viên ký: Pending. Candidate cuối: Pending. Kết luận đề xuất hiện tại: tài liệu và code cần được kiểm chứng trên nhánh; chưa đủ điều kiện khẳng định 100% nghiệm thu do remote CI/rehearsal/tiến độ ngoài repo cần bằng chứng.

Sau khi root chạy checks, cập nhật nhật ký evidence và điền trạng thái cụ thể tại đây. Với mục blocked, ghi điều kiện giải quyết, người phụ trách và bằng chứng còn thiếu. Chỉ cập nhật spreadsheet Hoàn thành/100% khi đáp ứng AC thật; không thay công thức hoặc sheet của người khác.

## 10. Đóng gói bằng chứng

scripts/package-handover.sh xuất tài liệu và bundle nguồn/artifact/evidence đã có, tạo SHA256 manifest. Tệp chưa có được ghi absent/pending thay vì tạo log giả. Kết quả export kiểm tra cấu trúc DOCX/PPTX, UTF-8 nguồn, số slide và PDF searchable; kiểm tra trực quan bằng viewer trước buổi bảo vệ. Không để file .env/secret/database dump trong bundle.
