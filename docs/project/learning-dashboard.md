# Dashboard học tập và theo dõi học viên

## Student

- `/Learning/Dashboard`: các khóa có enrollment hợp lệ, tiến độ hiện tại, lịch sử hoàn thành, kết quả đại diện và gợi ý ôn tập.
- `/Learning/Continue/{courseId}`: kiểm tra quyền và chọn lại Lesson mỗi lần mở. Giữ vị trí tiếp tục nếu Lesson vẫn khả dụng; nếu bị ẩn, dùng fallback của `CourseProgress`. Không còn Lesson thì về trang Course. GET không ghi progress hoặc đổi vị trí học.
- `/Assessment`: bài độc lập Published có owner chưa Disabled và bài trong khóa đã enroll; có thể tiếp tục attempt còn hạn sau khi nội dung bị rút. Không có quyền enrollment thì không thấy bài nội bộ của khóa.
- Course/Module/Lesson có liên kết Practice khả dụng; Course hiển thị các Assessment liên kết, ngoài phần Final đã có.

Progress dùng lại `ICourseService`: Lesson Published trong Module Visible. Tổng bằng 0 không có phần trăm; `CompletedAtUtc` là lịch sử và giữ nguyên khi progress giảm. Enrollment trả phí cần Payment Succeeded khớp Student/Course/giá VND của Order. Enrollment free cũ vẫn hợp lệ khi đổi khóa thành Paid.

### Kết quả và recommendation

BR11.1 chọn attempt Graded có điểm tổng cao nhất trên từng Assessment; đồng điểm chọn `FinalizedAtUtc` gần nhất, rồi Guid lớn hơn theo thứ tự .NET. Overall và skill breakdown cùng nguồn attempt. Không trộn điểm của nhiều bài để suy ra trình độ.

TBR08 chỉ phân tích skill có ít nhất 3 câu khác nhau, đồng thời có skill result đã lưu. BR13 dùng ngưỡng nhỏ hơn 60%. So sánh bằng tổng point đúng/tổng point của nhóm trước khi làm tròn; điểm hiển thị lấy từ kết quả đã lưu. Vì vậy một giá trị được hiển thị là 60% sau làm tròn vẫn có thể cần ôn tập nếu điểm gốc thấp hơn 60%.

Chỉ dùng nhóm có số câu và điểm tính lại khớp với skill result đã lưu; không kết luận điểm yếu từ dữ liệu lịch sử không nhất quán.

Gợi ý gồm Course có `CourseSkill` phù hợp và Practice/Assessment có `Question.PrimarySkill` phù hợp, giới hạn trong các khóa có quyền enrollment và còn Published/Unpublished. Course cần có Lesson khả dụng; Practice cần parent khả dụng; Assessment gợi ý cần Published. Không dùng tên khóa hoặc level để đoán skill. Không gợi ý nội dung Draft/Hidden/Archived, không gán CEFR/TOEIC. Nếu thiếu kết quả hoặc metadata phù hợp, UI hiển thị trạng thái rỗng và nêu bài đánh giá làm căn cứ cho từng gợi ý.

### Practice

`/Practice/Take/{id}` chỉ trả câu hỏi/options/stimulus, không trả đáp án đúng hoặc giải thích. POST `/Practice/Submit/{id}` cần CSRF, Student Active, enrollment hợp lệ, Course chưa Archived, Practice Published và Lesson/Module effective thuộc cùng owner. Kiểm tra toàn bộ câu/option, câu trùng và option từ câu khác trước khi ghi. Blank nhận 0, các câu có cùng trọng số.

Serializable transaction khóa Course → Practice để ghi một kết quả gần nhất theo unique `(StudentUserId, PracticeId)`; làm lại thay đáp án và điểm trong cùng transaction. `/Practice/Result/{id}` chỉ trả feedback của người đã nộp, giữ quyền đọc kết quả cũ khi Course Archived hoặc Practice bị rút. Student khác không xem được kết quả hoặc đáp án thông qua ID đó. Teacher authoring Practice/Assessment không được bổ sung trong luồng Student này; liên kết sử dụng schema parent/Course hiện có.

Dashboard có tối đa 20 kết quả Practice nộp gần đây để mở lại feedback kể cả khi bài bị rút. Trang Take chỉ hiển thị liên kết kết quả gần nhất nếu Student đã nộp bài đó.

## Teacher

Từ danh sách/quản lý khóa, mở `/TeacherCourses/Students/{courseId}` để xem học viên, progress, ngày tham gia và ngày hoàn thành lịch sử. Tìm theo tên, lọc lịch sử hoàn thành, 20 dòng/trang. Không trả email, credential, thông tin cá nhân khác hoặc chi tiết receipt.

Mọi query/pagination/aggregate lọc `CourseId` và `Course.OwnerTeacherUserId` trên server; role/status cũng được kiểm tra qua access service. Teacher khác và Student/Admin không có quyền đọc report của Course này.

Chỉ số toàn khóa không bị bộ lọc danh sách thay đổi:

- Enrollment hợp lệ; tách enrollment có Payment và không qua Payment, theo liên kết lúc cấp quyền.
- Số enrollment từng ghi nhận hoàn thành, độc lập với progress hiện tại.
- Tổng Order đã tạo, riêng số Order có receipt Succeeded khớp giá của Order và currency VND.
- Tổng tiền từ các receipt Succeeded hợp lệ, dùng quote của Order lúc mua. Pending/Failed/Cancelled không cộng tiền; thay giá Course không đổi số tiền lịch sử.

Các trang theo tài khoản dùng `no-store`. Thay đổi này không thêm schema/migration và không tự áp dụng migration/seed lên database ứng dụng.

## Kiểm chứng

`LearningDashboardFeatureTests` kiểm tra query/HTTP, quyền chéo owner/Student, enrollment trả phí sai, resume khi ẩn nội dung, metadata, skill trước làm tròn, feedback Practice và retake đồng thời. `SkillAnalysisRulesTests` kiểm tra ngưỡng 60%, tối thiểu 3 câu và mẫu số bằng 0.

Chạy bằng fixture hiện có; `ELP_TEST_SQLSERVER` chọn server local nhưng fixture luôn thay database bằng `EnglishLearningPlatformTests_<GUID>` riêng và dọn sau test. TRX của lượt chạy được lưu trong `artifacts/learning-dashboard-tests`, không dùng kết quả của candidate cũ để nghiệm thu thay đổi này.
