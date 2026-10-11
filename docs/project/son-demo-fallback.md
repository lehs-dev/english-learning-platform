# Demo và release gate của Sơn — 16/10/2026

Bản ứng viên có Student Assessment UI, Final block/Completion badge trên Learning và Admin query read-only. Kịch bản dưới dùng mã nguồn nhánh thực thi; kiểm chứng đúng candidate tại [SP14](../handover/SP14-test-acceptance.md) và [nhật ký evidence](../acceptance/evidence/son-sprint-16-10-evidence.md). Giờ/phân vai tổng duyệt là lịch đề xuất, chưa chứng minh rehearsal người thật đã diễn ra.

## Chuẩn bị môi trường

1. Dùng .NET 10, SQL Server 2022 và toàn bộ migration. Ghi branch/SHA trình diễn; kiểm tra working tree/cấu hình. Dùng database demo riêng khi cần lịch sử mới, không xóa dữ liệu hiện có để giả kết quả.
2. Đặt connection string qua environment/user-secrets, migrate đúng database:

   ```bash
   dotnet ef database update --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web
   dotnet ef migrations has-pending-model-changes --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web
   ```

3. Seed opt-in Development. Nhập mật khẩu kín để tránh shell history; seeder giữ mật khẩu lần đầu cho user đã tồn tại.

   ```bash
   read -r -s -p 'Mật khẩu demo local: ' LearningDemo__Password
   export LearningDemo__Password
   export LearningDemo__Enabled=true
   export ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development
   export Payments__Enabled=false CheckoutSandbox__Enabled=true
   dotnet run --project src/EnglishLearningPlatform.Web --no-launch-profile --urls http://localhost:8081
   # Sau khi dừng ứng dụng:
   unset LearningDemo__Password LearningDemo__Enabled CheckoutSandbox__Enabled Payments__Enabled
   ```

   Password đủ yêu cầu Identity. Hosted hợp lệ được ưu tiên, nên tắt hosted để demo local; local sandbox không thu tiền thật và không xác nhận Order hosted. Không in env/connection string/mật khẩu lên slide/log. SQL schema phải có trước seed.

4. Mở /health, /Courses. Dùng browser context riêng cho từng actor. Guest là context chưa đăng nhập, không có user Guest trong DB.

| Actor | Tài khoản | Dấu hiệu đúng |
| --- | --- | --- |
| Guest | Chưa đăng nhập | Catalog/detail công khai; protected Lesson dẫn tới Login. |
| Teacher 1 | teacher1@demo.example.test | /TeacherCourses có Free/Paid/Draft sở hữu, preview được. |
| Teacher đối chứng | teacher2@demo.example.test | Không được manage nội dung Teacher 1 dù có ID. |
| Student học/thi | student1@demo.example.test | Enroll Everyday English; đủ bài + Final đạt mới có badge/date. |
| Student mua khóa | student2@demo.example.test | Paid Order → sandbox success → Enrollment → Lesson; refresh không trùng. |
| Admin | admin1@demo.example.test | /Admin/Transactions tra đơn; /Admin/Users tra role/status; read-only. |

Seeder tạo đủ 5 tài khoản; 4 nhóm actor với Student tách hai hành trình. Seed lại backfill thiếu và giữ progress/attempt/password; không tạo enrollment Paid mẫu.

## Kịch bản browser demo

1. Guest mở /Courses, xem Free/Paid/giá, mở protected Lesson để thấy Login.
2. Student 2 đăng nhập, mua Demo · Business English (199.000 VND), tạo Order, mô phỏng success, xem kết quả và học Business introductions. Refresh rồi tra /Checkout/History.
3. Student 1 mở /Learning/Course/{courseId}, hoàn thành bài hiệu lực. Khi chưa đạt Final, progress có thể 100% nhưng chưa completion.
4. Mở khối Bài thi cuối khóa: duration, question count, attempts used/max, highest score, trạng thái. GET /Assessment/Take/{assessmentId} hiện overview/lịch sử; bấm Bắt đầu/Tiếp tục gửi POST /Assessment/Start/{assessmentId}.
5. Take?attemptId={attemptId} có radio single-choice, autosave POST Save, nút lưu và Submit, timer. Nộp POST /Assessment/Submit/{attemptId}; GET /Assessment/Result/{attemptId} xem tổng và skill breakdown. Lịch sử nằm trên overview Take; không có History action Assessment riêng.
6. Quay về khóa để thấy badge/ngày completion khi đủ bài + Final đạt. Deadline và pass do server quyết định, không do countdown/URL.
7. Teacher 1 quản lý/xem trước; Teacher 2 dùng context đối chứng quyền sở hữu. Admin mở /Admin/Transactions tìm email student2 hoặc Order ID, lọc trạng thái; mở /Admin/Users tìm role/status.

Everyday English Final: Published, 15 phút, tối đa 3 lượt, 5 câu bằng trọng số; đúng 4/5 đạt 80%. Grammar 3 câu, Vocabulary 2 câu. Đề SkillAssessment/TargetSkill Grammar phù hợp schema. Không đưa correct answers vào Student HTML; nội dung Assessment không trả lời giải từng câu.

Chuỗi paid → Final/completion có integration evidence riêng; khóa Paid demo chưa gán Final. Không trình bày hai demo account thành một paid-course Final scenario nếu dữ liệu không có. Theo [query contract](../architecture/assessment-student-queries.md), UI gọi IAttemptService bằng identity server, không tự chấm/ghi Attempt. Final graded, skills và completion commit cùng transaction; replay giữ điểm/time/history.

## Khi demo gặp sự cố

| Sự cố | Cách xử lý / evidence thay thế |
| --- | --- |
| SQL/schema chưa đủ | Migrate đúng DB, health; không xóa volume đang có dữ liệu. |
| Seed cũ thiếu Admin/Final | Bật seed lại với password cũ; giữ history/progress. |
| Media/mạng ngoài lỗi | Dùng Lesson Text và Final Grammar/Vocabulary không phụ thuộc media. |
| Hosted/provider ngoài lỗi | Restart app local Development với hosted tắt/sandbox opt-in; không xác nhận hosted Order qua local. |
| Attempt hết hạn/hết lượt | Giữ deadline/lịch sử; dùng actor/database demo riêng, không sửa điểm/timestamp. |
| Browser/UI lỗi | Ghi blocker, dùng backend contract/test evidence để giải thích; không ghi đó là UAT Pass. |
| Test/log sai SHA hoặc fail | Giữ chưa phát hành; sửa và chạy gate/CI lại trên committed candidate mới. |

Gate backend khi cần xác minh độc lập UI, build đúng revision trước:

```bash
dotnet build EnglishLearningPlatform.sln -c Release --no-restore
dotnet test tests/EnglishLearningPlatform.IntegrationTests/EnglishLearningPlatform.IntegrationTests.csproj \
  -c Release --no-build --filter 'FullyQualifiedName~PaidEnrollmentAssessmentAccessTests|FullyQualifiedName~CourseCompletionIntegrationTests|FullyQualifiedName~AssessmentQueryServiceTests|FullyQualifiedName~LearningDemoSeederTests|FullyQualifiedName~AuthorizationIntegrityAcceptanceTests'
```

Fresh Testcontainers không dùng ELP_TEST_SQLSERVER local:

```bash
env -u ELP_TEST_SQLSERVER dotnet test tests/EnglishLearningPlatform.IntegrationTests/EnglishLearningPlatform.IntegrationTests.csproj \
  -c Release --no-build --filter 'FullyQualifiedName~LearningDemoSeederTests'
```

## Chốt Release Candidate và hồ sơ

Commit candidate sau review. Chạy bash scripts/son-release-gate.sh <candidate-sha> trên clean tree; gate restore/Release build/unit/integration, lưu SHA/SDK/log/TRX và kiểm tra trước/sau. Fixture dùng database GUID riêng. Bỏ ELP_TEST_SQLSERVER để dùng Testcontainers.

CI PR/push main/workflow_dispatch lưu test-results, SHA checkout và TRX. RC phải đối chiếu SHA cuối và remote green run thật; PR CI có thể chạy merge SHA. CD GHCR chưa là deployment. Mỗi source change sau gate cần run lại; tag kỹ thuật không tự chứng nhận rehearsal/UAT chưa làm.

Bộ tài liệu [docs/handover](../handover/README.md) có SP11/SP13/SP14/SP15, báo cáo ba môn, deck 28 slides và notes. Xuất lại bằng bash scripts/package-handover.sh; bundle allowlist bằng --bundle. SP14 không lấy số 331 trong kế hoạch làm actual count, dùng TRX candidate.

## Hai buổi tổng duyệt đề xuất

Lượt 1: 15/10/2026, 09:00–11:30 Asia/Saigon. Mỗi owner chạy luồng và thử bảy nhóm sự cố trên, ghi issue/blocker, người sửa và kết quả rerun.

Lượt 2: 15/10/2026, 14:00–16:30 Asia/Saigon. Chạy cùng candidate/config demo; Sơn giải thích kiến trúc/Identity/HMAC/Serializable/completion, Khánh Learning/Teacher, Hoàng Assessment/scoring/history, Khang/Dương bấm Student/Admin. Đo thời lượng, chuyển vai và vấn đáp.

Ghi giờ/người tham gia/SHA/kết quả/ảnh hoặc video thật vào SP14/evidence ledger. Không đánh dấu hai buổi đã hoàn tất bằng walkthrough automation. Sheet chỉ cập nhật trạng thái đạt thực tế, đúng cell và đúng owner.
