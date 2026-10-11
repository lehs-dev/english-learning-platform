# SP15 · Hồ sơ yêu cầu và thiết kế

Nền tảng học tập và đánh giá năng lực tiếng Anh trực tuyến

Ngày: 11/10/2026 (Asia/Saigon). Nhóm trưởng: Lê Hoàng Sơn — 3123560077. Phạm vi bản ứng viên: hoàn thiện luồng Payment → Enrollment → Learning → Assessment → Result, security gate và hồ sơ release Công việc 10–12. Phân biệt yêu cầu đích với use case đã có UI trong candidate.

## 1. Bối cảnh và mục tiêu

Người học cần chọn khóa phù hợp, truy cập nội dung theo quyền, theo dõi tiến độ và biết kết quả năng lực. Teacher cần quản lý nội dung sở hữu. Admin cần tra cứu tài khoản/giao dịch, không can thiệp chuyên môn. Một mã nguồn phục vụ ba môn: Công nghệ .NET, Công nghệ lập trình hiện đại và Quản lý dự án phần mềm.

Mục tiêu kỹ thuật của candidate là giải quyết điều kiện hoàn thành khóa, bảo toàn lịch sử, thực thi quyền ở server, kiểm chứng transaction/replay/deadline và tái lập môi trường demo. Không dùng tên sandbox để ngụ ý tích hợp provider thương mại thật.

## 2. Phạm vi và actor

| Actor | Quyền và giới hạn |
| --- | --- |
| Guest | Xem catalog/detail Published, đăng ký Student; không có row Guest. |
| Student | Enrollment hợp lệ, học effective Lesson, progress, Attempt/Result riêng và checkout riêng. |
| Teacher | Chỉ authoring/preview Course thuộc OwnerTeacherUserId của mình. |
| Admin | Read-only tra cứu user/order/payment trong candidate; không sửa nội dung Teacher. |

Đã có UI candidate: login/register/profile, catalog/detail, free enroll, checkout/sandbox/history, learning/progress, course final overview, student taking/save/submit/result/history và admin query. Thiết kế đích còn có Practice authoring/feedback, Question Bank UI, Teacher Assessment/Final assignment/versioning, recommendation và quản trị đổi role/status; không nghiệm thu các tính năng đích chỉ từ domain/schema/service hiện có.

Ngoài phạm vi candidate: forum/livestream, thu tiền thật qua VNPay/MoMo, refund tự động, Speaking/Writing AI, chứng chỉ CEFR/TOEIC, multi-host load/outage verification và production deployment.

## 3. Danh mục yêu cầu chức năng

| Mã | Yêu cầu |
| --- | --- |
| FR01 | Catalog/detail công khai chỉ chứa nội dung đủ Published và metadata được phép. |
| FR02 | Đăng ký Student, login/logout, profile; đúng một active role và status check. |
| FR03 | Free ghi danh server-side idempotent; Paid cần receipt được server xác minh. |
| FR04 | Học mọi effective Lesson không ép thứ tự; hoàn thành/chưa hoàn thành cập nhật progress. |
| FR05 | Final overview hiển thị metadata/lượt/điểm cao nhất; taking không có IsCorrect. |
| FR06 | Start/resume, Save, Submit và deadline server; giới hạn lượt và một active Attempt. |
| FR07 | Result tổng và PrimarySkill breakdown, lịch sử của mình; không trả correct option. |
| FR08 | Completion chỉ khi effective Lessons đầy đủ và Final đạt nếu có, giữ mốc lịch sử. |
| FR09 | Teacher authoring/preview/status mutation đúng ownership. |
| FR10 | Admin read-only query/filter/pagination giao dịch và user. |
| FR11 | CI/gate và fresh seed có artifacts ràng buộc candidate SHA. |
| FR12 | Bộ hồ sơ, UAT và rehearsal minh bạch, không dùng test local thay evidence remote/manual. |

## 4. Yêu cầu phi chức năng

| Mã | Ràng buộc |
| --- | --- |
| NFR01 | Quyền role/status/ownership phải kiểm tra tại server; không chỉ ẩn nút. |
| NFR02 | Password Identity hash; env/user-secrets; HTTPS/cookie bảo vệ phiên thật. |
| NFR03 | MVC mutation có CSRF; webhook raw HMAC có exception được kiểm soát. |
| NFR04 | EF parameterization, Razor encoding và DTO tối thiểu, không lộ đáp án/secret. |
| NFR05 | Transaction đồng thời idempotent; unique/FK/check constraints; bảo toàn lịch sử. |
| NFR06 | Migration/seeder setup tái lập; release artifacts gắn SHA, SDK và test count thật. |
| NFR07 | Log hạn chế thông tin nhạy cảm; evidence được rà trước chia sẻ. |
| NFR08 | UI thông báo khi không có Lesson/không cấu hình thanh toán/hết lượt, có đường đi rõ. |

Không tuyên bố SLA/độ trễ/số người dùng đồng thời chưa đo. Tests concurrent nhiều scope trên SQL Server một host không thay thử nhiều host/process hoặc lỗi mạng ngoài thực.

## 5. Use case UC01 · Khám phá và ghi danh Free

Actor chính Guest/Student. Tiền điều kiện Course Published, owner không Disabled và Student Active khi ghi danh. Guest đọc catalog/detail; đăng nhập Student; POST EnrollFree có token; service kiểm tra course free và quyền; tạo Enrollment nếu chưa có; chuyển Learning.

Hậu điều kiện: đúng một Enrollment cho Student/Course, lịch sử enrollment cũ giữ nguyên. Ngoại lệ: khóa Paid phải đi checkout; Unpublished/Archived không cấp enroll mới; sai role/status hoặc token bị chặn. Truy vết FR01/02/03, AC-10.1.

## 6. Use case UC02 · Mua khóa Paid

Actor Student, adapter thanh toán. Student tạo Order; server chốt course/amount/provider/expiry theo metadata hợp lệ. Hosted trả checkout URL ký; local sandbox chỉ Development opt-in. Browser return chỉ hiển thị trạng thái. Server nhận receipt ký HMAC, kiểm tra raw bytes và Order/provider/amount/currency/time, ghi Payment/Enrollment cùng transaction nếu còn đủ điều kiện.

Hậu điều kiện: Payment thành công đã xác minh mở quyền học qua Enrollment; replay không duplicate. Receipt Failed không mở quyền; transaction ID dùng sai Order bị từ chối; receipt valid nhưng Course/User không còn đủ điều kiện có thể lưu lịch sử mà không cấp quyền mới. Truy vết FR03, AC-10.1/11.4.

## 7. Use case UC03 · Học và tiến độ

Actor Student. Tiền điều kiện Enrollment hợp lệ, Course/nội dung còn cho phép truy cập. Student chọn module/lesson theo bất kỳ thứ tự, đọc resource, POST Completion Completed/Incomplete có token. Service kiểm tra Lesson thuộc Course và effective availability; ghi progress, reconcile completion trong transaction.

Hậu điều kiện: tiến độ hiện tại chính xác; thiếu Final đạt thì chưa ghi completion. Archived giữ đọc theo quy tắc, không được tự ghi tiến độ mới trái lifecycle. Truy vết FR04/08, AC-10.2/10.4/10.5.

## 8. Use case UC04 · Thi Assessment

Actor Student. Đọc overview/taking/result bằng query DTO; Start tạo hoặc resume attempt hợp lệ, không vượt MaxAttempts; Save kiểm tra mỗi AssessmentQuestionId thuộc đề và SelectedOptionId thuộc câu. Null option xóa lựa chọn theo contract. Manual Submit hoặc DeadlineElapsed finalize từ đáp án đã lưu, chấm weighted points, ghi skill results và reconcile Course completion.

Hậu điều kiện: một grade ổn định và có lịch sử; đáp án muộn không đổi điểm; replay AlreadyGraded không đổi score/time, có thể repair missed completion theo guard. Truy vết FR05/06/07/08, AC-10.3/10.4/10.5.

GET không tạo attempt. Deadline trong DB là nguồn tin cậy; UI countdown/autosave không quyết định grade. Result không lộ correct option/explanation. Lazy finalize khi đọc xử lý attempt hết hạn theo service, không tính điểm trong view.

## 9. Use case UC05 · Teacher quản lý nội dung

Actor Teacher sở hữu. Tạo/sửa Course metadata, module, lesson, resource; thay đổi thứ tự/trạng thái; preview. Mọi action kiểm tra OwnerTeacherUserId và active role/status. Teacher khác/Student/Admin không được mutate cùng ID.

Hậu điều kiện: published content đúng lifecycle và không làm mất dữ liệu học/thi. Final assignment/versioning Teacher UI là phạm vi owner cần bổ sung/kiểm chứng riêng; guard completion không thay bằng chứng gắn/gỡ Final qua UI. Truy vết FR09, AC-11.3.

## 10. Use case UC06 · Admin tra cứu

Actor Admin Active, một role. Mở /Admin/Transactions để search email/title/orderGuid, filter Pending/Succeeded/Failed/Cancelled và phân trang 20 rows. Mở /Admin/Users để search name/email, filter Active/Locked/Disabled và xem roles.

Hậu điều kiện: trả metadata tối thiểu read-only; không có correct answers hay endpoints sửa content. Việc thay đổi user status/role do hạ tầng Identity hiện thực khi được gọi không đồng nghĩa candidate có UI mutation Admin. Truy vết FR10, AC-11.3/12.3.

## 11. Quy tắc nghiệp vụ đã chốt

| Quy tắc | Mô tả |
| --- | --- |
| BR01 | Mỗi user có đúng một role active; Locked/Disabled không được dùng phiên cũ ở request kế tiếp. |
| BR02 | Owner và StudentId lấy từ principal/server; browser không tự chọn chủ sở hữu. |
| BR03 | Enrollment unique Student/Course; Paid linked payment cần valid receipt/quote. |
| BR04 | Effective Lesson là Published trong Visible Module; không ép học tuần tự; tập rỗng không Complete. |
| BR05 | Progress hiện tại độc lập với CompletedAtUtc lịch sử. |
| BR06 | Final cùng Course/Owner, Published/type/PassingScore hợp lệ; kết quả Graded đạt mới tính. |
| BR07 | Grade weighted correct points/total points; pass dùng score thô trước rounding display. |
| BR08 | Deadline server immutable của Attempt; max lượt và one active kiểm tra trong transaction. |
| BR09 | Save câu/option đúng scope; Submit/timeout grade một lần và persist skills cùng transaction. |
| BR10 | Một graded passing Final đủ cho completion; failed retake sau không phủ định passing history. |
| BR11 | Completion marker ghi lần đầu, giữ qua thay nội dung/incomplete; không xóa Attempt/grade history. |
| BR12 | Webhook ký đúng raw body; return page không xác nhận Payment; replay safe. |
| BR13 | Admin không mặc nhiên là Teacher và không xem correct answers. |
| BR14 | Secret/evidence/test status trung thực, count/SHA là dữ liệu thực chứ không mục tiêu kế hoạch. |

## 12. Hợp đồng dữ liệu và nhất quán

Guid domain identity, DateTimeOffset UTC, money decimal(18,2), score hiển thị decimal(5,2). DTO taking chứa question/option ID/content/order/stimulus, không IsCorrect. Result chứa score/pass/status/time và SkillBreakdowns; attempt legacy chưa có skill rows trả danh sách rỗng, không query-regrade.

Unique constraints là hàng rào cuối; checks status/score/option model và ownership dùng nhiều tầng. Các quan hệ chéo Course → Final Assessment và Assessment → Course phải đúng owner/course trước assignment/completion. Serializable + thứ tự khóa nhất quán Course → Assessment → Attempt tránh grading/completion race. Transaction executor giữ lỗi chính khi rollback/dispose lỗi và không log secrets.

## 13. Thiết kế UI và route

| Màn hình | Đường dẫn |
| --- | --- |
| Catalog và detail | /Courses; /Courses/Details/{id} |
| Đăng nhập / hồ sơ | /Account/Login; /Account/Register; /Profile |
| Learning | /Learning; /Learning/Course/{id}; /Learning/Lesson/{id} |
| Checkout và lịch sử | /Checkout/Index?courseId={id}; /Checkout/Result/{id}; /Checkout/History |
| Assessment | /Assessment/Take/{assessmentId}; /Assessment/Result/{attemptId} |
| Teacher | /TeacherCourses; /TeacherCourses/Manage/{id} |
| Admin query | /Admin/Transactions; /Admin/Users |

POST Start/Save/Submit, EnrollFree/Completion và Teacher mutations có token; Webhook /payments/webhook có HMAC. ID trong đường dẫn không chứng minh quyền; mọi query/service lọc principal/resource eligibility.

## 14. Quản lý thay đổi và nghiệm thu

Công việc 10 (10%) đóng luồng/Completion; Công việc 11 (8%) security/CI; Công việc 12 (8,9%) fresh setup/UAT/RC/handover. Trọng số tổng 26,9% là khối lượng cá nhân trong kế hoạch, không thay số AC Passed. Root giữ branch riêng để review; code đã sửa phải build/test cùng SHA trước RC.

SP14 là ma trận truy vết và biên bản; evidence ledger là nguồn kết quả thật. Rehearsal 15/10 là lịch đề xuất, không tạo chữ ký/người tham gia giả. Spreadsheet được cập nhật đúng file/cell và trạng thái thực sau đối chiếu, không sửa công thức/sheet thành viên khác. Tính năng ngoài candidate cần issue owner riêng và acceptance riêng.
