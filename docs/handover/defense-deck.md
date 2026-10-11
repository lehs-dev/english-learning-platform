# Nền tảng học tiếng Anh
- Học tập · Đánh giá · Hoàn thành khóa
- ASP.NET Core MVC + SQL Server 2022
- Sơn và nhóm triển khai · 16/10/2026
> Mở đầu: một mã nguồn phục vụ ba môn; deck của candidate, evidence thật theo SP14.
---
# Bài toán cần giải quyết
- Nội dung học và tiến độ nằm trong một hành trình
- Quyền truy cập phải theo ghi danh và thanh toán
- Điểm thi và lịch sử cần giữ nhất quán
> Nêu vấn đề cụ thể của người học: đã học đủ nhưng còn cần đạt Final.
---
# Bốn nhóm người dùng
- Guest: tìm hiểu khóa công khai
- Student: mua, học, thi và xem kết quả
- Teacher: quản lý nội dung sở hữu
- Admin: tra cứu người dùng và giao dịch
> Student 1/2 là hai hành trình, không phải role khác; Guest không có row trong DB.
---
# Phạm vi bản ứng viên
- Catalog · Identity · Checkout sandbox
- Learning và tiến độ không ép thứ tự
- Final Assessment, Result và Completion
- Admin tra cứu · hồ sơ release
> Phân biệt UI candidate với Question Bank/Practice/Teacher Assessment authoring đích.
---
# Luồng xuyên suốt
- Payment → Enrollment → Learning
- Learning → Assessment → Result
- Đủ bài học + Final đạt → Completion
> Demo bằng hai Student; return URL không tự cấp quyền.
---
# Một mã nguồn, ba môn
- .NET: MVC, Identity, EF và migration
- Hiện đại: DI, async, test và CI
- QLDA: phạm vi, phụ thuộc và nghiệm thu
> Dùng cùng một hành vi để giải thích ba góc nhìn, không tạo ba sản phẩm riêng.
---
# Kiến trúc bốn lớp
- Web: request, policy và Razor
- Application: interface, DTO và use case
- Domain: entity và invariant
- Infrastructure: EF, Identity và transaction
> Trace Controller → contract → implementation → SQL; không chấm điểm trong view.
---
# Request qua middleware
- HTTPS và routing
- Authentication → Authorization
- MVC → service → SQL Server
- Response theo quyền và trạng thái
> Giải thích thứ tự AuthN trước AuthZ; DbContext scoped theo request.
---
# Mô hình Learning
- Course → Module → Lesson → Resource
- Enrollment nối Student với Course
- LessonProgress lưu Completed/Incomplete
- Chỉ Published trong Visible Module tính tiến độ
> Tập bài hiệu lực rỗng không được xem là 100%; học không tuần tự.
---
# Mô hình Assessment
- Assessment → Question và Points
- Attempt giữ deadline, status và score
- Answer chỉ thuộc câu của đề đang làm
- SkillResult được lưu cùng grade
> Correct options nằm server; Student DTO không có IsCorrect.
---
# Identity và phân quyền
- Một active role: Student, Teacher hoặc Admin
- Locked/Disabled chặn phiên cũ
- Teacher ownership kiểm tra ở server
- Admin không sửa nội dung Teacher
> Không coi ẩn nút là authorization; stale cookie test kiểm tra request kế tiếp.
---
# CSRF, XSS và secrets
- Form mutation có antiforgery token
- Razor encode, EF parameterization
- Cookie HttpOnly, SameSite và Secure HTTPS
- Secret từ environment hoặc user-secrets
> Webhook là ngoại lệ CSRF có HMAC raw body; không hiển thị secret trên slide/log.
---
# Thanh toán sandbox
- Server chốt Order, amount và provider
- Hosted callback xác minh HMAC raw bytes
- Receipt hợp lệ mới cấp Enrollment
- Replay không tạo giao dịch/ghi danh trùng
> Hosted là hợp đồng riêng; local Development không thu tiền thật.
---
# Transaction và concurrency
- Serializable cùng thứ tự khóa nhất quán
- Unique index là hàng rào database
- Grade, skills, completion commit cùng nhau
- Lỗi thì rollback; replay giữ lịch sử
> Course → Assessment → Attempt; không nhận kiểm thử một host là multi-host.
---
# Quy tắc hoàn thành khóa
- Có ít nhất một bài học khả dụng
- Mọi bài khả dụng đã Completed
- Nếu có Final: graded và raw score đạt
- CompletedAtUtc ghi lần đầu và được giữ
> Thêm bài mới giảm progress hiện tại nhưng không xóa mốc lịch sử.
---
# Điểm tổng và kỹ năng
- Tổng điểm theo trọng số Points
- Passed so với điểm thô trước làm tròn
- Breakdown theo PrimarySkill
- Assessment không hiện đáp án từng câu
> Ví dụ raw 69,996% hiển thị 70,00% vẫn fail threshold 70%.
---
# Deadline và lưu đáp án
- Start/Resume tối đa một Attempt active
- Autosave và lưu thủ công
- Deadline UTC do server quyết định
- Submit hoặc hết hạn chấm một lần
> Đồng hồ client chỉ hiển thị; nói rõ đáp án chưa lưu có thể không được tính.
---
# Demo Guest và Teacher
- Guest xem catalog, detail và giá
- Protected Lesson chuyển sang Login
- Teacher xem khóa của mình và preview
- Teacher khác bị từ chối resource
> Khánh trình bày Learning/Teacher; dùng teacher2 đối chứng.
---
# Demo Student thanh toán
- Student 2 chọn Business English
- Tạo Order 199.000 VND và sandbox success
- Trang kết quả → Khóa học của tôi
- Refresh giữ một Enrollment
> Khang/Dương bấm; giữ mã đơn để Admin tra cứu sau.
---
# Demo Student thi Final
- Student 1 hoàn thành bài Everyday English
- Final: 15 phút, 3 lượt, 5 câu
- Đúng ít nhất 4/5 đạt ngưỡng 80%
- Result và Completion badge/date
> Hoàng giải thích scoring/skills; không dùng đáp án học viên để lộ correct metadata.
---
# Demo Admin tra cứu
- Giao dịch: tìm email, khóa hoặc mã đơn
- Lọc trạng thái và phân trang
- Người dùng: email, role và status
- Read-only, không sửa nội dung/đáp án
> Admin routes /Admin/Transactions và /Admin/Users, 20 hàng/trang.
---
# Kiểm thử tự động
- Unit cho rule, scoring và validator
- Integration trên SQL Server thật
- Tamper, role, replay, deadline và race
- Tổng số lấy từ TRX candidate
> Không lặp số 331 của kế hoạch; report count, SHA và log thật từ SP14.
---
# CI và release gate
- Restore → Release build → Unit → Integration
- Candidate SHA, SDK và TRX trong artifact
- Gate yêu cầu committed clean tree
- Remote green run cần URL và SHA khớp
> CI PR có thể chạy merge SHA; CD GHCR chưa là deploy website.
---
# Fresh setup và vận hành
- Database riêng → migration → model check
- Seeder Development đủ 5 tài khoản
- Seed lại giữ password, progress và attempt
- Health, backup và restore drill
> SP13 có lệnh vận hành; không reset dữ liệu hiện có để làm đẹp demo.
---
# UAT và tổng duyệt
- Năm hành trình, bốn nhóm actor
- Browser evidence riêng với test backend
- Hai rehearsal người thật ngày 15/10
- Pending nếu chưa có kết quả thực tế
> SP14 có biên bản 09:00–11:30 và 14:00–16:30, Asia/Saigon; đây là lịch đề xuất.
---
# Quản lý công việc 10–12
- CV10: luồng và Completion · 10%
- CV11: security và CI · 8%
- CV12: RC, UAT và bàn giao · 8,9%
- Trạng thái dựa trên AC và evidence
> Tổng 26,9% là trọng số cá nhân, không phải mức đạt hiện tại; sheet cập nhật đúng cell.
---
# Bàn giao và bài học
- SP11 manual · SP13 vận hành
- SP14 nghiệm thu · SP15 yêu cầu/thiết kế
- Báo cáo ba môn, deck và manifest
- Contract, lịch sử và SHA cần rõ từ đầu
> Mở artifact nguồn/exports; giải thích lịch sử completion khác progress hiện tại.
---
# Giới hạn và phát triển tiếp
- Provider thật và production deploy chưa nghiệm thu
- Teacher Assessment authoring/versioning cần scope riêng
- Mở rộng Practice, recommendation và load tests
- Trao đổi và vấn đáp
> Kết thúc bằng hành vi chạy được và evidence, không nhận scope đích là đã xong.
