# Hợp đồng đọc Assessment cho Student

`IAssessmentQueryService` cung cấp dữ liệu cho giao diện của Dương/Hoàng. Đăng ký scoped trong Infrastructure; controller lấy `studentId` từ claims của người đăng nhập và dùng policy Student. Không nhận Student ID từ form/query để thay thế identity server.

| Phương thức | Dữ liệu | Điều kiện |
| --- | --- | --- |
| `GetAssessmentOverviewAsync` | Cấu hình, số lượt đã dùng, điểm cao nhất, attempt đang làm và deadline cũ | Student Active, đúng một role Student; Assessment Published; course-linked phải cùng owner và Enrollment hợp lệ. Course/owner không còn khả dụng chỉ cho overview nếu có attempt đang làm còn hạn. |
| `GetTakingSheetAsync` | Đề theo thứ tự, options theo thứ tự, stimulus text và lựa chọn đã lưu | Cùng chủ attempt, Student hợp lệ, Enrollment hợp lệ nếu course-linked, InProgress và chưa tới deadline server. Giữ resume sau withdrawal giống `StartAsync`. |
| `GetAttemptResultAsync` | Điểm tổng, điểm theo kỹ năng và thời điểm/lý do finalize đã lưu | Cùng chủ attempt, Student hợp lệ, Graded và metadata finalize đầy đủ. Giữ kết quả lịch sử khi Course/Assessment bị rút hoặc Enrollment thay đổi. |

Không tìm thấy hoặc không được phép trả `null`. Taking sheet hết hạn cũng trả `null`, không tự chấm: UI có thể gọi `IAttemptService.FinalizeAsync` bằng identity server rồi chuyển sang Result theo status. Deadline trả nguyên giá trị đã lưu; đồng hồ client chỉ để hiển thị. `TimeProvider` của query mặc định là `TimeProvider.System`.

DTO đề thi không có `IsCorrect`, `Explanation` hoặc entity/navigation. Truy vấn chỉ SELECT dữ liệu cần trả, không SELECT cột đáp án đúng. Questions/options được sắp theo `OrderIndex` rồi `Id`. Không đưa đáp án chi tiết vào Result. `PassingScore = null` (ví dụ Placement) có nghĩa không áp dụng đạt/trượt; UI không diễn giải `Passed = false` thành trượt nếu không có ngưỡng.

Các read dùng `AsNoTracking`, không ghi DB, không mở transaction hoặc nới khóa của `IAttemptService`. Start/Save/Finalize vẫn dùng Serializable và thứ tự khóa Course → Assessment → Attempt. Các read không thay thế kiểm tra quyền/deadline bên write service.

Finalize mới ghi `AttemptSkillResults` cùng giao dịch với OverallScore và CourseCompletion. Mỗi PrimarySkill được chấm theo trọng số Points bằng `IExamScoringService`, làm tròn phần trăm 2 chữ số; QuestionCount gồm cả câu chưa trả lời. Replay Graded giữ nguyên điểm, breakdown và thời điểm. Các attempt Graded cũ chưa có breakdown được trả danh sách rỗng; query không tính lại hoặc sửa lịch sử.

Result lấy title/CourseId/PassingScore từ Assessment hiện tại; schema chưa snapshot các trường metadata này theo từng attempt. Điểm, Passed, skill breakdown và finalize metadata lấy từ lịch sử đã persist.

Các màn hình Assessment/Result, stimulus audio và Admin UI do owner tương ứng tích hợp sau; hợp đồng này không tạo route UI mới. Xem [bằng chứng SON-01–07](../acceptance/evidence/son-sprint-16-10-evidence.md) và [kịch bản demo](../project/son-demo-fallback.md).
