# Attempt và kiểm thử Assessment — phần việc Dương

Ngày kiểm tra: 14/10/2026. Nhánh: `feature/duong-assessment-attempt`.

## Phạm vi thay đổi

Luồng Start/Save/Resume, deadline server, scoring và bảng điểm kỹ năng đã có trong repo.
Thay đổi này bổ sung điều hướng từng câu, đếm/tô trạng thái đã trả lời, xác nhận nộp
có cảnh báo câu bỏ trống, audio Listening và liên kết từ Result đến gợi ý ôn tập
trên Dashboard. Gợi ý dùng quy tắc và quyền học hiện có, dựa trên kết quả đại diện
điểm cao nhất; không tự thay bằng kết quả của lần thi vừa nộp.

Khi hết thời gian, dialog được đóng và tự nộp. Việc chấm và từ chối đáp án muộn
vẫn do server quyết định. Lưu/Resume giữ nguyên Attempt và deadline, không cấp lượt mới.

## Lỗi backend được tái hiện và sửa

`AssessmentQueryService.GetTakingSheetAsync` chỉ lấy `Stimulus.ContentText`, bỏ
`ResourceUrl` của stimulus Audio. Vì vậy câu Listening có audio trong database
nhưng trang làm bài không có trình phát.

Test mới `ListeningAudio_IsAvailableOnlyToEnrolledAttemptOwner_AndResumeKeepsDeadline`
đã thất bại trước sửa tại kiểm tra `<audio`. Bản sửa thêm `AudioUrl` vào DTO an toàn,
trả URL cho stimulus Audio và chỉ cho phép URL HTTP/HTTPS tuyệt đối. Giao diện render
audio với controls, không tự phát. Không đưa đáp án đúng hoặc explanation vào DTO.
Không có thay đổi schema/migration.

## Kết quả kiểm thử

| Nhóm | Kết quả | Phạm vi |
| --- | --- | --- |
| Integration | 80/80 đạt | UI flow, query, lifecycle, paid enrollment/access; có test mới audio và 3 URL không hợp lệ |
| Unit | 6/6 đạt | ExamScoringService, SkillAnalysisRules |
| Chromium script | 4/4 đạt | Xác nhận/hủy, lưu thủ công, timeout, lỗi autosave |
| Browser E2E | 6/6 đạt | Guest, Student, Teacher, Admin; thanh toán sandbox, Resume, 80% score, completion, ownership/privacy |

Các test cũ được chạy lại; đây không phải 96 test mới viết. E2E sử dụng database
demo riêng và web ở cổng 8081. Nguồn có thay đổi chưa commit khi kiểm thử;
không coi SHA base trong report là SHA của toàn bộ thay đổi này.

Log integration: `tests/EnglishLearningPlatform.IntegrationTests/TestResults/duong-assessment.trx`.
Report/ảnh E2E: `tests/browser/TestResults/browser-uat/`.
Các thư mục TestResults được Git bỏ qua. Chạy lại để tạo bằng chứng trên máy khác.

## Chạy lại

```powershell
dotnet test tests/EnglishLearningPlatform.IntegrationTests --filter "FullyQualifiedName~AssessmentUiFlowTests|FullyQualifiedName~PaidEnrollmentAssessmentAccessTests|FullyQualifiedName~AssessmentQueryServiceTests|FullyQualifiedName~AssessmentLifecycleAcceptanceTests" --logger "trx;LogFileName=duong-assessment.trx" -- RunConfiguration.CollectSourceInformation=false
dotnet test tests/EnglishLearningPlatform.UnitTests --filter "FullyQualifiedName~ExamScoringServiceTests|FullyQualifiedName~SkillAnalysisRulesTests" -- RunConfiguration.CollectSourceInformation=false
cd tests/browser
npm ci
npx playwright install chromium
npm run test:assessment
```

Integration mặc định tạo SQL Server container riêng. Nếu Docker không ổn định,
có thể truyền `ELP_TEST_SQLSERVER` bằng môi trường với connection string của SQL Server
local. Fixture vẫn tạo/xóa database tên ngẫu nhiên của chính nó, không dùng database
ứng dụng. Trên máy kiểm tra, cần `127.0.0.1` thay cho `localhost` để kết nối SQL.
Tắt CollectSourceInformation tránh lỗi đọc symbol của test runner trên ổ Z.
Để chạy E2E thực tế, làm theo `tests/browser/README.md`, dùng database demo riêng mới seed.

## Giới hạn và phần cần review

- Test script dùng fetch mô phỏng; E2E và integration kiểm tra server thật.
- Test audio xác minh URL, HTML player, quyền truy cập và Resume. Chưa xác minh phát
  âm thanh từ một nhà cung cấp media bên ngoài; URL thực tế cần còn hoạt động.
- NuGet có cảnh báo NU1902/NU1903 cho dependency Pkcs 9.0.11 đã có trước thay đổi.
- Team cần review code và Dương cần tự diễn tập trước khi ghi hoàn thành/nghiệm thu.

## Giải thích khi trình bày

1. Trình duyệt tính đồng hồ từ deadline và thời gian server, không lấy giờ máy làm mốc.
2. Chọn đáp án kích hoạt autosave; các request được xếp hàng để giữ thứ tự. Submit chờ
   request đang chạy rồi gửi lựa chọn mới nhất; server vẫn chặn mọi thay đổi sau deadline.
3. Resume đọc đáp án đã lưu và deadline cũ; GET không tiêu tốn lượt thi.
4. Audio bị thiếu ở DTO backend, nên chỉ thêm HTML audio sẽ không giải quyết lỗi.
5. Khóa trả phí chỉ cấp quyền Assessment sau payment được server xác minh và có
   enrollment hợp lệ. Pending/fail/cancel/chữ ký sai vẫn bị chặn.
