# Demo và release gate của Sơn — 16/10/2026

Phạm vi: backend/hợp đồng của Sơn. Giờ tổng duyệt và phân vai bên dưới là đề xuất, chưa phải bằng chứng đã rehearsal. Assessment/Result UI của Dương/Hoàng, Dashboard của Khánh và Admin UI phải được tích hợp trước demo toàn bộ bằng trình duyệt.

## Chuẩn bị môi trường

1. Dùng .NET 10, SQL Server 2022 và toàn bộ migration trong repo. Kiểm tra `git status` và ghi SHA đang trình diễn.
2. Cấu hình connection string qua environment/user-secrets. Áp dụng migration:

   ```bash
   dotnet ef database update --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web
   ```

3. Seed opt-in trong Development. Nhập mật khẩu qua prompt để tránh lưu trong lịch sử shell; với tài khoản demo đã tồn tại, dùng mật khẩu từ lần seed đầu, seeder không reset mật khẩu.

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

   Password phải đủ yêu cầu Identity. Khi bật local sandbox, không cấu hình hosted gateway vì hosted hợp lệ được ưu tiên. Không in env hoặc connection string lên slide/log.

4. Mở `/health`, `/Courses`, đăng nhập các actor và thử form mutation thiếu CSRF. Guest là trình duyệt chưa đăng nhập, không có role/user Guest trong DB.

| Actor | Tài khoản | Thao tác và dấu hiệu đúng |
| --- | --- | --- |
| Guest | Không đăng nhập | Xem Catalog/detail, protected Lesson dẫn tới Login |
| Teacher | `teacher1@demo.example.test` | `/TeacherCourses` có Free/Paid/Draft; preview được nội dung của mình |
| Student | `student1@demo.example.test` | Đã enroll Everyday English; học Lesson Published, đánh dấu Completed; còn thiếu Final thì chưa CompletedAt |
| Student mua khóa | `student2@demo.example.test` | Business English: tạo Order → mô phỏng fail/retry/success → Enrollment → đọc Lesson; refresh không cấp trùng |
| Admin | `admin1@demo.example.test` | Login thành công; policy `/api/probe/policy/single-active-role` trả 200; quản lý nội dung Teacher bị chặn. Chỉ dùng Admin UI khi Dương đã tích hợp |

## Đề Final mẫu và bàn giao UI

Everyday English có Final Published, 15 phút, tối đa 3 lượt, 5 câu trọng số bằng nhau; đúng 4 câu đạt 80%. Grammar có 3 câu, Vocabulary có 2 câu. Đề dùng SkillAssessment với TargetSkill Grammar để thỏa constraint SQL hiện có. Business English có Lesson học được sau thanh toán; chuỗi paid → final/completion có bằng chứng tự động riêng, không seed Enrollment Paid.

Dương/Hoàng dùng [hợp đồng truy vấn Student](../architecture/assessment-student-queries.md). Start/Save/Finalize lấy Student ID từ claims và đi qua `IAttemptService`; UI không ghi Attempt hoặc tự chấm. Phải hoàn thành mọi Lesson khả dụng **và** đạt Final mới có `CompletedAtUtc`. Lượt Graded và timestamp đã ghi không bị sửa bởi refresh/submit lặp; payment invalidated sau Start vẫn được finalize lịch sử nhưng không cấp CourseCompletion.

## Khi demo gặp sự cố

| Sự cố | Cách xử lý / bằng chứng thay thế |
| --- | --- |
| SQL chưa có metadata Order | Chạy migration ở trên; kiểm tra `/health`. Không xóa volume dữ liệu đang dùng |
| Seed cũ chưa có Admin/Final | Bật seed lại với password cũ; seeder backfill mà giữ password/progress/attempt |
| Media ngoài không tải được | Demo Lesson Text; dùng nội dung Grammar/Vocabulary của Final, không phụ thuộc audio ngoài |
| Payment hosted/provider ngoài lỗi | Dùng local opt-in Development, `Payments__Enabled=false`; tắt app rồi bật lại đúng cấu hình. Không xác nhận Order hosted qua local |
| Attempt hết hạn/hết lượt | Giữ nguyên deadline/attempt history; demo bằng Student khác hoặc database demo riêng. Không sửa timestamp/xóa attempt để giả kết quả |
| UI Assessment hoặc Admin chưa có | Trình bày contract/service và chạy gate tự động bên dưới; ghi rõ đây là minh chứng backend, chưa phải browser UAT |
| Test fail hoặc log không khớp SHA | Giữ trạng thái chưa phát hành, sửa lỗi rồi chạy lại gate/CI trên candidate mới |

Gate backend độc lập UI (build trước, không dùng DLL cũ):

```bash
dotnet build EnglishLearningPlatform.sln -c Release --no-restore
dotnet test tests/EnglishLearningPlatform.IntegrationTests/EnglishLearningPlatform.IntegrationTests.csproj \
  -c Release --no-build --filter 'FullyQualifiedName~PaidEnrollmentAssessmentAccessTests|FullyQualifiedName~AssessmentQueryServiceTests|FullyQualifiedName~LearningDemoSeederTests|FullyQualifiedName~AuthorizationIntegrityAcceptanceTests'
```

Fresh SQL Server từ Testcontainers, không dùng DB local cấu hình cho test:

```bash
env -u ELP_TEST_SQLSERVER dotnet test tests/EnglishLearningPlatform.IntegrationTests/EnglishLearningPlatform.IntegrationTests.csproj \
  -c Release --no-build --filter 'FullyQualifiedName~LearningDemoSeederTests'
```

## Chốt Release Candidate

Commit candidate sau review. Chạy `bash scripts/son-release-gate.sh <candidate-sha>` trên working tree sạch: restore → Release build → toàn bộ unit/integration; lưu SHA, SDK, log và TRX vào thư mục tạm. `ELP_TEST_SQLSERVER` nếu dùng luôn được fixture thay bằng database GUID riêng. Bỏ biến này để dùng Testcontainers. Script kiểm tra lại SHA và working tree trước/sau gate, không merge/tag/publish.

CI tự chạy PR/push main hoặc dùng workflow_dispatch. Artifact `test-results` ghi SHA checkout thực tế cùng TRX. CI PR có thể chạy merge commit; để chốt RC, đối chiếu green run trên SHA cuối của main sau merge. Mỗi thay đổi code sau gate cần chạy gate lại. Tag RC chỉ sau green CI và rehearsal; không dùng SHA base hoặc log baseline làm bằng chứng candidate.

Đề xuất tổng duyệt hai lượt: lượt 1 mỗi owner chạy luồng của mình, ghi blocker theo SON-01–07; lượt 2 chạy cùng candidate và cấu hình demo, có người bấm UI và người giải thích transaction/authorization. Sơn giải thích verified payment, khóa Serializable, idempotency và completion; Khánh trình bày Learning; Hoàng trình bày scoring/history; Dương trình bày timer/Result/Admin. Ghi thời gian thực tế (Asia/Saigon), SHA, kết quả và đường dẫn bằng chứng vào [nhật ký nghiệm thu](../acceptance/evidence/son-sprint-16-10-evidence.md).
