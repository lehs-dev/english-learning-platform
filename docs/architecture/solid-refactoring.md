# Kiến trúc sau đợt chuẩn hóa SOLID

Cập nhật ngày 22/10/2026 trên nhánh `refactor/solid-architecture`.

Đợt rà soát này xử lý các điểm tập trung trách nhiệm, phân nhánh theo loại nội dung,
phụ thuộc gateway cụ thể, kiểm tra role lặp lại và SQL khóa dữ liệu rải trong service.
Các route MVC, DTO/view model, giá trị enum và schema database được giữ nguyên.

## Ranh giới trách nhiệm

| Phần việc | Contract dùng cho code mới | Implementation |
| --- | --- | --- |
| Catalog và thông tin khóa công khai | `ICourseCatalogService` | `CourseCatalogService` |
| Mở khóa/bài học, enrollment và progress | `IStudentLearningCourseService` | `StudentLearningCourseService` |
| Soạn và xuất bản khóa học/nội dung | `ICourseAuthoringService` | `CourseAuthoringService` |
| Tra cứu và bắt đầu checkout | `ICheckoutOrderService` | `CheckoutOrderService` |
| Xác minh và đối soát callback | `IPaymentConfirmationService` | `PaymentConfirmationService` |
| Mô phỏng thanh toán local | `ISandboxCheckoutService` | `SandboxCheckoutService` |
| Đọc role hiện tại của tài khoản Active | `IActiveUserRoleReader` | `ActiveUserValidator` |
| Kiểm tra tài khoản Active theo role | `IActiveUserValidator` | `ActiveUserValidator` |
| Đọc thông tin tài nguyên để xét quyền | `ILearningResourceAccessReader` | Các reader trong `Learning/Access` |

`CourseService` và `CheckoutService` là các façade chuyển tiếp để giữ nguyên hai
composite contract cũ. Những service chuyên trách là các instance riêng theo scope;
chúng dùng chung `AppDbContext` của request. Controller sử dụng các contract nhỏ
phù hợp với thao tác của mình.

`CourseQueries` dùng chung các phép chiếu dữ liệu; `CourseProgressService` quản lý
truy vấn progress và gọi quy tắc reconciliation hiện có. Các helper này không quay
ngược về façade nên không tạo vòng phụ thuộc.

## Thêm loại nội dung hoặc tài nguyên

Để thêm cách soạn nội dung, tạo implementation `ICourseContentHandler` và đăng ký
vào DI. `CourseContentHandlerRegistry` chọn handler theo `ContentKind`;
`CourseAuthoringService` không có switch phân luồng theo loại nội dung.
Handler chịu trách nhiệm kiểm tra parent, đọc input, validation, chuẩn bị thay đổi
và cung cấp các phần tử cần sắp xếp. Chỉ implementation hỗ trợ xóa resource mới
cần implement `ICourseResourceDeletionHandler`.

Handler không tự mở transaction, gọi `SaveChanges` hoặc commit. Coordinator giữ
thứ tự kiểm tra quyền/parent, transaction, khóa Course, lưu dữ liệu và reconciliation.
`AffectsCompletion` quyết định có chạy reconciliation khi thay đổi nội dung hay không.
`CourseContentOrderPlan` giữ hai lượt cập nhật chỉ số để tránh xung đột unique index;
ID trùng, thiếu, thuộc parent khác hoặc chỉ số tạm vượt giới hạn đều bị từ chối.

Để hỗ trợ loại tài nguyên trong kiểm tra quyền, đăng ký thêm
`ILearningResourceAccessReader`. Reader chỉ chiếu owner, Course, trạng thái và
visibility; `LearningAccessService` giữ quy tắc quyền Teacher/Student và enrollment.
Registry từ chối đăng ký trùng loại. Thêm một handler/reader mới không cần sửa
coordinator tương ứng; enum và UI cần được bổ sung nếu đó là chức năng sản phẩm mới.

## Chấm điểm và thanh toán

`IScoringStrategy` mặc định dùng trọng số Assessment. Practice nhận
`IPracticeScoringStrategy`, có thể thay policy riêng qua DI mà không đổi cách chấm
Assessment. Cả hai implementation hiện tại dùng chung cách xét đáp án và làm tròn;
đáp án trống, sai, ngoài câu hỏi và quy tắc response cuối cùng của Assessment được giữ.

Gateway hosted implement `IWebhookPaymentGateway`; sandbox implement
`ISandboxPaymentGateway`. Hai capability không yêu cầu phương thức giả trả `null`.
Khi thay gateway hosted, đăng ký `IPaymentGateway` và `IWebhookPaymentGateway`
về cùng một instance để luồng tạo checkout và xác minh callback dùng cùng provider.

Web đọc request qua `PaymentWebhookProcessor`; Application không phụ thuộc
`HttpRequest` hay `IActionResult`. Chữ ký hosted được kiểm tra trên raw bytes.
`PaymentConfirmationService` giữ cùng pipeline transaction cho receipt/enrollment,
phân biệt hosted và local để bảo toàn cách xử lý replay và audit hiện tại.

## Quyền và Persistence

`ActiveUserQueries` tập trung query tài khoản Active có đúng một role được hỗ trợ.
Các phép kiểm tra trong authoring, practice, assessment, administration và audio
đều dùng query này hoặc validator chung. Query không tracking để kiểm tra lại
trong transaction đọc được trạng thái tài khoản hiện tại. Kiểm tra security stamp,
đăng nhập và cập nhật Identity vẫn thuộc các service Identity tương ứng.

`SqlServerLockingQueries` chứa toàn bộ SQL update/key-range lock và application lock.
Caller tiếp tục giữ transaction, thứ tự lấy khóa, tracking, Include và yêu cầu
Single/SingleOrDefault. Query lấy khóa khi được thực thi; không chuyển việc thực thi
ra ngoài transaction. Không có raw locking SQL trong các service nghiệp vụ.

URL phát audio và MIME detection nằm ngoài Domain. Các entity có Teacher owner
implement `IOwnedResource` bằng explicit property để không thêm cột EF.
Audit query thuộc `IAdminQueryService`; API quản trị cũ vẫn chuyển tiếp để giữ tương thích.

## Kết quả kiểm chứng

- Build toàn solution ở Debug và Release: 0 warning, 0 error.
- 322/322 unit test mục tiêu pass, không skip.
- Kiểm tra model EF với migration snapshot: không có thay đổi schema.
- Kiểm tra các attribute route/action của controller và `git diff --check`: đạt.
- Không chạy bộ test toàn cục, integration test hoặc browser test.

Bộ test mục tiêu bao phủ chấm điểm, quyền và query role, DI/façade, handler nội dung,
sắp xếp hai lượt, SQL sinh ra có tham số, callback/sandbox, audio, audit và validators.
Các test SQL chỉ tạo lệnh trong bộ nhớ, không kết nối database; cạnh tranh khóa và
callback thực tế trên SQL Server không nằm trong phạm vi kiểm chứng của đợt này.

Lệnh build:

```bash
dotnet build EnglishLearningPlatform.sln --configuration Debug --no-restore
dotnet build EnglishLearningPlatform.sln --configuration Release --no-restore
```

Lệnh unit test đã dùng:

```bash
dotnet test tests/EnglishLearningPlatform.UnitTests/EnglishLearningPlatform.UnitTests.csproj --no-build --no-restore --filter 'FullyQualifiedName~Scoring|FullyQualifiedName~QuestionUsageValidator|FullyQualifiedName~AssessmentDefinitionValidator|FullyQualifiedName~ActiveUser|FullyQualifiedName~RefactoringCompatibility|FullyQualifiedName~ResourceOwnerAuthorizationHandler|FullyQualifiedName~StimulusAudioResource|FullyQualifiedName~StimulusAudioStore|FullyQualifiedName~AdminAuditForwarding|FullyQualifiedName~Commerce|FullyQualifiedName~PracticeServiceAuthorization|FullyQualifiedName~Course|FullyQualifiedName~LearningAccessService|FullyQualifiedName~SqlServerLockingQueries|FullyQualifiedName~QuestionQueryServiceTests|FullyQualifiedName~DependencyInjection'
```
