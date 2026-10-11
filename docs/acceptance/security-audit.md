# Kiểm toán bảo mật — Công việc 11

Ngày kiểm tra: 11/10/2026 (giờ Việt Nam). Phạm vi: mã nguồn ứng dụng, controller MVC/API, cấu hình CI và toàn bộ lịch sử Git **reachable ở checkout hiện tại**. Kết quả được gắn với SHA trong từng báo cáo; báo cáo quét working tree trước commit không thay thế evidence cho release candidate đã commit.

## Thay đổi đã thực hiện

- Cookie Identity: `HttpOnly=true`, `SameSite=Lax`, `SecurePolicy=Always` trong Production/Staging. Development dùng `SameAsRequest` để chạy demo HTTP local; khi truy cập HTTPS vẫn phát cookie Secure. Không dùng môi trường Development cho deployment production.
- MVC có `AutoValidateAntiforgeryTokenAttribute` toàn cục. Mọi action mutation trình duyệt khai báo `[ValidateAntiForgeryToken]`; POST vào action chưa giới hạn HTTP verb cũng bị global filter bảo vệ.
- `.gitignore` loại trừ `.env`, `.env.*` ngoại trừ `.env.example`, `appsettings.Production.json`, cấu hình local, file IDE và kết quả test.
- Connection string không còn password mặc định trong `appsettings.json`; deployment cung cấp qua environment hoặc user-secrets. Demo password cũng lấy từ cấu hình ngoài repo. Local sandbox dùng key ngẫu nhiên theo process; hosted HMAC key bắt buộc được cấp ngoài repo.
- Admin có policy riêng và service guard xác minh tài khoản Active với **đúng một role Admin** trong database. Chỉ có GET tra cứu giao dịch/tài khoản; DTO không chứa password hash, security stamp, signing secret hay câu hỏi/đáp án.
- CI chạy khi push `main`, `feat/**`, khi PR vào `main` và khi dispatch; checkout full history, audit secret, Release build, unit/integration tests và upload TRX, SHA, SDK, audit CSV/TXT.

## Ma trận toàn bộ controller

| Controller | Truy cập | Mutation / CSRF | Kiểm soát tài nguyên |
| --- | --- | --- | --- |
| Account | Login/Register/AccessDenied anonymous; Logout authenticated | Register, Login, Logout có token | Register chỉ tạo Student; return URL phải local |
| Admin | Admin + SingleActiveRole | Chỉ GET | Service xác minh role/status trong DB; projections chỉ đọc |
| Assessment | Student + SingleActiveRole | Start, Save, Submit có token | Enrollment/owner attempt được kiểm tra qua query/write services; taking sheet không chứa `IsCorrect` |
| ArchitectureProbe | DI anonymous; Teacher/SingleActiveRole trên probes tương ứng | Chỉ GET | Không trả nội dung khóa học/đề thi |
| Checkout | Student + SingleActiveRole; webhook anonymous | CreateOrder, Start, Simulate có token; webhook ngoại lệ mô tả dưới | Đơn hàng gắn current StudentUserId, dữ liệu amount/provider/status kiểm tra server-side |
| Courses | Catalog/detail anonymous; EnrollFree Student | EnrollFree có token | Public chỉ Published; ghi danh free có service guard, idempotent |
| Home | Public | Không mutation; global filter bảo vệ các unsafe verb | Catalog dùng public query; Error không trả nội dung exception |
| Learning | Student/Teacher + SingleActiveRole; manage Teacher | Completion Student có token | `ILearningAccessService` và CourseService xác minh enrollment/teacher owner, visibility |
| Profile | Authenticated | Update có token | ID từ claim; chỉ bind FullName của chính user |
| TeacherCourses | Teacher + SingleActiveRole | Edit, Content, DeleteResource, Reorder, Publish, Unpublish, Archive có token | CourseService xác minh `OwnerTeacherUserId`; Admin không đạt Teacher policy |

Có 20 action POST khai báo: 19 action trình duyệt có antiforgery token và 1 webhook server-to-server. `SecurityConfigurationTests.AllDeclaredMutationEndpoints_RequireAntiforgery_ExceptBoundedSignedWebhook` phát hiện việc bỏ token hoặc thêm ngoại lệ ngoài webhook.

### Ngoại lệ webhook

`POST /payments/webhook` có `[AllowAnonymous]`, `[IgnoreAntiforgeryToken]`, giới hạn body 16 KiB và kiểm tra chữ ký server. Không dùng cookie/session để xác thực callback. Hosted gateway dùng `HMACSHA256.HashData(key, rawBody)` và so sánh `FixedTimeEquals` **trước** deserialize JSON; thay đổi khoảng trắng làm chữ ký cũ không hợp lệ. Local sandbox ký canonical envelope bằng key ngẫu nhiên và chỉ được bật trong Development. Hai giao thức không dùng chéo chữ ký/provider. Callback trùng được xử lý idempotent; callback mâu thuẫn không ghi đè kết quả thanh toán/enrollment đã lưu.

## SQL injection, XSS và log

- Các truy vấn dùng EF LINQ với parameters. SQL locking theo ID dùng `FromSqlInterpolated`; `ExecuteSqlRaw` trong commerce chỉ là SQL cố định `sp_getapplock`, không ghép input của request. Admin search/status/page được xử lý trong SQL, không tải toàn bộ DB để lọc client-side.
- Không tìm thấy `Html.Raw` trong Views. Razor encode text, including title/email/search được hiển thị ở Admin. Test đưa `<script>` vào title và xác minh browser HTML chỉ chứa entity encoded. Nội dung lesson/resource tiếp tục dùng kiểu dữ liệu và URL validation của CourseService.
- `CheckoutTransactionExecutor` không ghi exception object/message/body/signature. Log chỉ có operation, reference GUID, phase, error type, HResult/SqlNumber và commit status. Controller callback chỉ ghi enum outcome. Test fault injection hiện hữu xác minh logger không chứa sensitive marker và exception object là null.
- `son-release-gate.sh` redact `Password`, `Pwd`, `SigningSecret` trong console/log stream. Redaction này không tự sửa TRX được test framework ghi trực tiếp; cần kiểm tra artifact trước khi chia sẻ. Quét repo bao gồm các evidence logs được track, không bao gồm logs ngoài checkout hay file ignored.

## Quét secrets và bằng chứng

Lệnh tái lập:

```bash
python3 scripts/security-audit.py --self-test
python3 scripts/security-audit.py --output TestResults/security-audit.csv
dotnet test tests/EnglishLearningPlatform.IntegrationTests/EnglishLearningPlatform.IntegrationTests.csproj \
  -c Release --no-build \
  --filter 'FullyQualifiedName~SecurityConfigurationTests|FullyQualifiedName~AdminReadOnlyTests' \
  --logger 'trx;LogFileName=security-admin.trx' --results-directory TestResults
```

Scanner tự kiểm tra 14 tình huống: credential SQL/HMAC/environment, provider token, private key, placeholder, allowlist fixture chỉ hợp lệ trong `tests/` và HTML entity encoding. HTML/HTM được decode entities trước khi quét để bắt credential thật được encode, đồng thời tránh nhận nhãn prompt mật khẩu là credential. Báo cáo chỉ lưu path/line/kind và `[REDACTED]`, không xuất giá trị nghi vấn. Quét lịch sử xử lý từng blob duy nhất từ `git rev-list --objects --all`; kết quả tại thời điểm triển khai: **89 commit reachable, 503 historical blobs, 0 suspicious patterns**. Public placeholders và synthetic test fixtures được review, không diễn giải là credentials đang dùng.

Bằng chứng focused suite và scan nằm trong [thư mục security](evidence/security/). Kết quả focused suite sau fix EF projection: **17/17 Passed**, gồm flag cookie cho Development/Staging/Production, CSRF matrix/global filter, Guest/Student/Teacher bị từ chối Admin, tra cứu/pagination/filter Admin, HTML encoding, read-only dữ liệu và Locked/Disabled Admin bị revoke ở request tiếp theo.

## Tiêu chí cần evidence ngoài checkout

Audit pattern không thể chứng minh “zero secrets” tuyệt đối: không kiểm tra Git object unreachable, nhánh remote chưa fetch, credentials/logs ngoài repo hoặc các dạng secret chưa nằm trong patterns. Nếu phát hiện credential thật đã commit, cần revoke/rotate ở hệ thống sở hữu và xử lý lịch sử theo quyết định nhóm; không tự rewrite history chung.

Remote CI green run phải gắn với **cùng SHA** của release candidate, cùng artifact TRX/SHA và URL Actions run. Workflow cấu hình đúng và tests local Passed chưa đủ đánh dấu AC-11.5 remote green. Kết quả remote/final release gate được cập nhật trong hồ sơ release của nhóm sau khi chạy thực tế; không có dữ liệu giả thay cho evidence.
