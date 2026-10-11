# Bằng chứng triển khai SON-01–SON-07

Ngày thực hiện: 11/10/2026 (Asia/Saigon). Nguồn: kế hoạch kỹ thuật Sơn 11–16/10 được cung cấp và [tiến độ công việc.xlsx](https://docs.google.com/spreadsheets/d/1E_nodHM2VwFwqbon_2l6pzlnI02t1Z33/edit), đọc qua Google Drive connector. File nguồn là Excel trên Drive, không phải native Google Sheet; giữ nguyên file nguồn. Phân công Sơn: kiến trúc, Identity/Authorization, Commerce, đồng thời/toàn vẹn và phát hành; UI Assessment/Admin/Dashboard vẫn thuộc owner module.

## Phiên bản và môi trường

- Working tree ban đầu sạch trên `main`, base SHA `80a2ccb0936128bca8dec0de5b19fe6d19e6fd70`.
- Đã checkout `feature/son-sprint-16-10`. Kết quả dưới đây áp dụng cho thay đổi trong working tree nhánh này; SHA base không phải SHA release candidate đã commit.
- .NET SDK `10.0.401`, EF CLI `10.0.12`, SQL Server 2022 Docker. Sandbox shell không khởi động được vì user namespaces bị tắt; lệnh chạy bằng môi trường được approval review cho phép.
- Baseline Release build: 0 warning, 0 error. Baseline **84 unit + 189 integration = 273 Pass**, không skip; [unit log](son-sprint-16-10/baseline-unit.log), [integration log](son-sprint-16-10/baseline-integration.log).
- Regression dùng `ELP_TEST_SQLSERVER` từ connection local qua environment, không in credential. Fixture luôn tạo/xóa DB `EnglishLearningPlatformTests_<GUID>` riêng; không dùng database ứng dụng cho tests.
- [Source digest manifest](son-sprint-16-10/implemented-source.sha256) ghi hash của code/tests/CI/script được kiểm tra. Từ repo root có thể chạy `sha256sum -c docs/acceptance/evidence/son-sprint-16-10/implemented-source.sha256`. Đây là snapshot working tree, không thay thế SHA candidate đã commit.

## Ma trận audit và đầu ra

| ID | Gap ban đầu | Đầu ra / kiểm chứng | Trạng thái |
| --- | --- | --- | --- |
| SON-01 | Migration `AddCheckoutOrderMetadata` Pending trên DB local; cần baseline | Đã áp dụng migration local theo kế hoạch, không tạo migration mới. EF list sau update không còn Pending; integration kiểm tra schema/constraints khớp model | Hoàn thành |
| SON-02 | Chưa có query DTO/service cho UI Student | `AssessmentTakingModels`, `IAssessmentQueryService`, DI scoped, safe projection AsNoTracking; test quyền/ownership/paid enrollment/deadline/ordering/no answer key; [hợp đồng bàn giao](../../architecture/assessment-student-queries.md) | Hoàn thành |
| SON-03 | Chưa có bằng chứng cả chuỗi verified paid → Lesson → Final → Completion | `PaidEnrollmentAssessmentAccessTests` chạy thanh toán local HMAC thực, xác thực Enrollment, học Lesson, thi/chấm và CompletedAt; fail/pending/cancel/tampered receipt không cấp quyền | Hoàn thành backend; review PR/UI của owner còn phụ thuộc tích hợp |
| SON-04 | Result theo kỹ năng chưa được persist | Finalize lưu weighted skill breakdown cùng grade/completion trong transaction hiện có; concurrent Start/Submit/Callback và replay giữ ID/timestamp; rollback completion cũng rollback grade/skill results | Hoàn thành |
| SON-05 | Cookie có thể giữ role cũ trong security-stamp validation interval | Cookie recheck role DB và role claim ngay mỗi request; API/MVC stale cookie bị từ chối. Test HMAC raw bytes thay whitespace, CSRF/ownership/lock/disable và redacted error logs | Hoàn thành gate tự động |
| SON-06 | Seed cũ không có Admin/Final và return sớm không backfill | Admin dùng LearningDemo:Password; Final 5 câu, 15 phút, 3 lượt, 80%; seed mới/backfill giữ password/progress/attempt và không grant Paid. Guest ẩn danh. HTTP login/policy/content qua dữ liệu seed và fresh SQL Docker | Hoàn thành gate tự động fresh Docker; browser rehearsal chờ owner UI |
| SON-07 | Thiếu fallback/demo gate có SHA | [Cheatsheet](../../project/son-demo-fallback.md), script `scripts/son-release-gate.sh` kiểm tra candidate sạch và SHA trước/sau; CI có manual dispatch và artifact SHA/SDK/TRX | Đã chuẩn bị; chờ commit/review/green remote CI, merge P0, tag RC và hai lượt rehearsal |

## Migration local

Lệnh đã chạy:

```bash
dotnet ef migrations list --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web --configuration Release --no-build
dotnet ef database update --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web --configuration Release --no-build
```

Trước: `20261008035103_AddCheckoutOrderMetadata (Pending)`. Update trả exit `0`, `Applying migration ...`, `Done.` Sau: đủ 6 migration, không Pending. [Update log](son-sprint-16-10/migration-update.log), [list sau update](son-sprint-16-10/migration-after.log). Đây là thay đổi schema local được kế hoạch yêu cầu; không xóa/reset database hoặc volume hiện có.

## Quy tắc được giữ và sửa

Read service không mở transaction, không auto-finalize/ghi dữ liệu và không SELECT `IsCorrect`/`Explanation`. Deadline cũ được trả nguyên giá trị, còn hạn kiểm tra lại sau read. Course-linked overview/taking yêu cầu cùng owner và Enrollment hợp lệ; role/status kiểm tra từ DB. Result chỉ của Student owner, metadata finalize phải đầy đủ; không bịa timestamp khi dữ liệu thiếu. Score/Passed/finalize/skill history giữ nguyên khi replay; title/CourseId/PassingScore là metadata Assessment hiện tại vì schema chưa snapshot các trường này.

Serializable và khóa Course → Assessment → Attempt không thay đổi. Existing attempt vẫn resume/finish theo lifecycle cũ sau withdrawal/archive. Đúng như `PaymentInvalidatedBeforeFinalize_DoesNotRecordCourseCompletion`, attempt đã bắt đầu có thể finalize lịch sử bởi Student Active sở hữu attempt sau payment invalidation, nhưng completion không được cấp nếu Enrollment không hợp lệ. Grade cũ thiếu breakdown trả danh sách rỗng, không chấm lại lịch sử.

SkillScore dùng cùng `IExamScoringService` trên mỗi nhóm PrimarySkill, tính theo Points, làm tròn 2 chữ số; QuestionCount gồm câu chưa trả lời. Không tạo duplicate `(AttemptId,Skill)` khi double submit. Regression `CompletionUpdateFailure_RollsBackGradeAndCompletion_ThenNewScopeRetrySucceeds` kiểm chứng rollback cả skill rows, retry ghi một lần.

Cookie role mới có hiệu lực request kế tiếp ngay cả khi SecurityStamp chưa đổi: `ChangedRole_RejectsStaleCookieImmediately_WithoutSecurityStampChange`. Cookie cũ bị sign-out, login lại có quyền role mới. CSRF tiếp tục bắt buộc cho browser POST; webhook là ngoại lệ dùng bounded HMAC raw body. `WebhookHmac_BindsExactRawBytes_WhenJsonWhitespaceChanges` xác nhận cùng JSON semantic nhưng khác bytes bị từ chối với signature cũ; signature đúng bytes mới được nhận.

Secrets payment/demo lấy từ cấu hình server, sandbox key ngẫu nhiên trong process; không có EnableSensitiveDataLogging. `CheckoutTransactionExecutor` chỉ log metadata lỗi, không Exception/Message/body/signature. Fault-injection regression kiểm tra marker nhạy cảm không xuất hiện. Log/TRX cần kiểm tra trước chia sẻ; redaction console trong script chỉ là best effort, không thay thế chính sách logging này.

## Verification thực chạy

```bash
dotnet build EnglishLearningPlatform.sln -c Release --no-restore
dotnet test tests/EnglishLearningPlatform.UnitTests/EnglishLearningPlatform.UnitTests.csproj -c Release --no-build --no-restore
dotnet test tests/EnglishLearningPlatform.IntegrationTests/EnglishLearningPlatform.IntegrationTests.csproj -c Release --no-build --no-restore
env -u ELP_TEST_SQLSERVER dotnet test tests/EnglishLearningPlatform.IntegrationTests/EnglishLearningPlatform.IntegrationTests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~LearningDemoSeederTests'
git diff --check
bash -n scripts/son-release-gate.sh
```

Regression lần đầu: 84 unit Pass; integration 245 Pass/3 Fail. Ba lỗi fixture mới: hai lần thêm role thứ hai bị index `IX_AspNetUserRoles_UserId` từ chối; so sánh GUID theo SQL order với .NET order. Đã sửa fixture sang replacement/no-role và sort sau materialize, giữ nguyên constraints; bỏ một case multi-role không thể tạo qua schema hợp lệ. [Log lần đầu](son-sprint-16-10/regression-integration-first.log).

Kết quả cuối cùng, mọi lệnh exit `0`:

| Gate | Actual | Bằng chứng |
| --- | --- | --- |
| Release build | 0 warning, 0 error | [build log](son-sprint-16-10/build-final.log) |
| Unit | 84/84 Pass, không skip | [unit log](son-sprint-16-10/regression-unit.log) |
| Integration đầy đủ | 247/247 Pass, không skip; runner 41s | [integration log](son-sprint-16-10/regression-integration.log), [247 outcome theo tên case](son-sprint-16-10/integration-case-results.log) |
| Query Student | 35/35 Pass trong bộ đầy đủ | `AssessmentQueryServiceTests` |
| Paid → Final → Completion | 14/14 Pass trong bộ đầy đủ | `PaidEnrollmentAssessmentAccessTests` |
| Seeder local | 4/4 Pass trong bộ đầy đủ | `LearningDemoSeederTests` |
| Role/API/schema | 12/12 Pass trong bộ đầy đủ, gồm 4 case stale-cookie mới | `AuthorizationIntegrityAcceptanceTests` |
| Fresh Docker | 4/4 Pass, không skip; runner 49s | [fresh log](son-sprint-16-10/fresh-docker-seeder.log), [container lifecycle](son-sprint-16-10/fresh-docker-events.log) |
| Diff / script | `git diff --check`, `bash -n` Pass | [static check log](son-sprint-16-10/static-check.log) |

Tổng regression **331/331 Pass**, tăng 58 case từ baseline 273; raw-byte HMAC và rollback skill assertions cũng nằm trong integration đầy đủ. TRX gốc ở `/tmp/elp-son-sprint/{regression-unit,regression-integration,fresh-docker-seeder}.trx`; log giữ trong repo đã kiểm tra/loại configured secret literals.

Fresh run bỏ `ELP_TEST_SQLSERVER`, dùng SQL Server 2022 Testcontainers: mỗi test seeder có factory/database riêng; Docker lifecycle ghi 4 SQL Server container mới và cleanup. Kiểm tra fresh seed + HTTP Guest/Teacher/Student/Admin, backfill dữ liệu cũ, real attempt scoring/completion và role mismatch. Không reset container/volume dev hiện có.

## Việc còn cần bằng chứng người thực hiện

UI thi/timer/Result và Admin chưa có trong source ở base này; không nhận là đã làm browser UAT toàn chuỗi. Seed Admin chỉ chứng minh login/active-role policy, chưa chứng minh Admin UI. Media ngoài chưa kiểm tra trực quan/offline. Provider ngoài không được gọi trong phiên này; local mock/hosted HMAC contract tests không thay cho giao dịch sandbox provider thật.

GitHub connector đã tìm PR đang mở trong `lehs-dev/english-learning-platform`: không có kết quả tại thời điểm kiểm tra, nên chưa có PR Hoàng/Dương cụ thể để review/merge. GitHub CLI local chưa đăng nhập; việc đọc trạng thái này dùng connector đã kết nối.

Chưa sửa file tiến độ Excel, gửi thông báo cho thành viên, review/merge PR remote, chạy GitHub CI cho thay đổi chưa push, tag RC hoặc tổ chức rehearsal. Kịch bản và script đã có để hoàn tất trên candidate sau tích hợp. Không lấy SHA base hoặc log local để tuyên bố remote CI xanh.
