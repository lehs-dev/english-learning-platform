# A3 — Timeout, Resume và deadline

Ngày kiểm tra: **21/10/2026, Asia/Bangkok**.

**Kết luận: đạt sáu mục A3 của luồng Student.** Start/Resume giữ một lượt đang
làm và deadline gốc; server từ chối đáp án từ đúng deadline; Submit và xử lý hết
giờ đồng thời chỉ chấm một lần. Lượt cũ vẫn hoàn thành được sau khi đề bị rút.

## 1. Đã bổ sung gì và vì sao

Các phần Start, Resume, lưu đáp án và khóa giao dịch đã có. A3 tái sử dụng chúng,
không viết lại công thức chấm, không thêm bảng hoặc migration.

| Vị trí | Thay đổi | Công dụng |
| --- | --- | --- |
| [IAttemptService.cs](../../src/EnglishLearningPlatform.Application/Assessment/IAttemptService.cs) | Thêm `FinalizeExpiredAsync`. | Phân biệt yêu cầu chấm khi hết giờ với Submit chủ động; mở trang trước hạn không được tự nộp bài. |
| [AssessmentAttemptService.cs](../../src/EnglishLearningPlatform.Infrastructure/AssessmentAttempts/AssessmentAttemptService.cs) | Dùng `TimeProvider` đã đăng ký trong DI; hai đường finalize dùng chung phần kiểm quyền, khóa và chấm. | Đồng hồ server quyết định thời hạn; test cố định được từng tick mà không Sleep. Chỉ chấm `InProgress` đã đến deadline; đọc lại bài đã chấm không sửa điểm/timestamp. |
| [AssessmentController.cs](../../src/EnglishLearningPlatform.Web/Controllers/AssessmentController.cs) | Take/History/Result gọi write service để xử lý lượt của Student đã hết hạn. | Không cần browser quay lại gửi Submit mới có kết quả. Không nhận đáp án/deadline từ GET, không gọi Start trên GET. History tải lại dữ liệu sau khi chấm. |
| [_AttemptHistory.cshtml](../../src/EnglishLearningPlatform.Web/Views/Assessment/_AttemptHistory.cshtml) | Sửa comment cho đúng hành vi mới. | View chỉ hiển thị; controller/service xử lý hết hạn. Không thay HTML/CSS. |

Query service vẫn chỉ đọc. Kiểm tra owner và trạng thái/deadline được thực hiện
lại trong write service sau khi lấy khóa, không tin dữ liệu từ trang cũ.
Giữ thứ tự khóa **Course → Assessment → Attempt** và transaction hiện có.
Start/Save/Submit thủ công vẫn kiểm tra CSRF; lazy-finalize chỉ dùng đáp án đã lưu.

Hai test có `new AssessmentAttemptService(...)` được thêm `TimeProvider.System`
cho đúng constructor, không thay kịch bản hoặc nới expected:
`AssessmentLifecycleAcceptanceTests`, `TeacherAssessmentScopeTests`.

## 2. Đối chiếu sáu yêu cầu A3

Mỗi dòng dưới là kiểm thử tự động, không phải biên bản UI thủ công.

| Mã | Tiền điều kiện / thao tác | Kết quả mong đợi và thực tế | Test nguồn |
| --- | --- | --- | --- |
| A3-01 | Student gửi 5 Start đồng thời, sau đó Start lại khi đã dùng lượt duy nhất. | PASS: một Started, các request còn lại Resume cùng AttemptId/deadline; chỉ một Attempt trong DB. | `AssessmentAttemptStartTests.ConcurrentStarts_ReuseOneActiveAttempt`; `ActiveAttempt_AtMaxAttempts_ReturnsExistingAttempt`. |
| A3-02 | Lưu/sửa/bỏ chọn, đăng nhập lại rồi Resume. | PASS: giữ lựa chọn mới nhất đã lưu, không nhân đôi Answer, không đổi StartedAt/deadline. | `AssessmentUiFlowTests.StartResume_NewLoginAndRepeatedGets_KeepOneAttemptSavedAnswersAndOriginalDeadline`; `SaveJson_ChangeRetryAndClear_KeepOneAnswerPerQuestionAndPreserveOtherChoices`. |
| A3-03 | GET overview và F5 màn hình làm bài nhiều lần, kể cả gửi deadline giả từ client. | PASS: GET không Start, không tiêu thụ lượt; deadline vẫn bằng StartedAt server + Duration đã lưu. | `AssessmentUiFlowTests.GetOverview_DoesNotConsumeAttempts_AndStartRequiresStudentEnrollmentAndCsrf`; test StartResume ở A3-02. |
| A3-04 | Lưu một đáp án đúng trong đề 5 câu equal points; đổi thành sai ở deadline -1 tick, đúng deadline, +1 tick. | PASS: trước hạn lưu sai nên chấm 0%; đúng/sau hạn từ chối thay đổi và chấm 20% từ đáp án đúng cũ. Deadline không đổi; replay không đổi timestamp/điểm. | `AssessmentAttemptStartTests.Save_OneTickBeforeAtAndAfterDeadline_UsesServerClockAndPreservesOriginalDeadline` — 3 biến thể mới. |
| A3-05 | Clock đúng deadline; Submit và lazy-finalize dùng hai scope chạy đồng thời. | PASS: một Graded + một AlreadyGraded, cùng 20% và DeadlineElapsed; một Attempt/Answer/SkillResult, không kéo dài hạn. HTTP Save/Submit muộn cũng giữ đáp án cũ. | `AssessmentAttemptStartTests.ManualSubmitAndLazyExpiry_AtExactDeadline_GradeOnceWithoutExtendingTime` — mới; `AssessmentUiFlowTests.ConcurrentLateSaveAndSubmit_IgnoreChangedAnswersAndGradeOneResult` — tái sử dụng. |
| A3-06 | Đã Start trước khi Assessment Unpublish/Archive hoặc Course bị rút; Resume/chấm khi còn hạn hoặc quá hạn. | PASS: giữ deadline và hoàn thành lượt cũ; không mở Start lượt mới trên đề đã rút. Take quá hạn có/không attemptId đều chuyển sang Result mà không cần POST. | `AssessmentLifecycleAcceptanceTests.ExistingAttempt_ResumesOrFinalizesAfterAssessmentWithdrawn`; `ExistingCourseAttempt_PreservesDeadlineAndCanFinishAfterArchive`; `AssessmentUiFlowTests.ExpiredGet_LazilyGradesSavedAnswersWithoutPost_AndReplayKeepsResult`. |

Đối chứng thêm cho đường đọc mới:

- `AssessmentUiFlowTests.ReadPages_LazilyGradeOnlyOwnExpiredAttempt_AndNeverSubmitAnActiveAttempt`:
  Result/History trước hạn không chấm; URL người khác bị từ chối và không chấm bài
  của họ; hết hạn chấm đúng bài của mình 80%; F5 giữ timestamp, deadline và số hàng.
- `AssessmentQueryServiceTests.History_QueryIsReadOnly_HttpFinalizesExpiredButPreservesOtherRows`:
  query thuần không ghi; HTTP chỉ chấm lượt InProgress hết hạn, không sửa các hàng
  Submitted/AutoSubmitted hoặc lượt còn hạn.
- `AssessmentUiFlowTests.ExpiredPost_GradesSavedAnswersOnceAndIgnoresLateChanges`:
  Save/Submit sau deadline không ghi đáp án muộn hoặc chấm lại.

Chỉ thêm 3 method test (6 biến thể), mở rộng một theory Take từ 2 thành 4 biến
thể và cập nhật test History theo hành vi HTTP mới. Không viết lại các test
Start/Resume/concurrency đã có.

## 3. Bằng chứng chạy thực tế

Mốc gốc: `3f45ac1501c05db4b7a6b70975c37c1add05e8c2`, cộng thay đổi QA2/A3 trong
working tree **chưa commit**. Đây là kết quả local, chưa phải CI GitHub.

| Kiểm tra | Kết quả / artifact local |
| --- | --- |
| Build solution Release | PASS: 0 warning, 0 error. |
| Toàn bộ unit | PASS **390/390**, 0 fail/skip; 17:59:56–18:00:00 (+07:00); `TestResults/qa3/qa3-unit.trx`. |
| Integration trọng tâm A3 | PASS **11/11**, 0 fail/skip; 18:00:01–18:00:52; `TestResults/qa3/qa3-focus.trx`. |
| Integration hồi quy | PASS **229/229**, 0 fail/skip; 18:02:28–18:04:59; `TestResults/qa3/qa3-regression.trx`. |
| Browser fixture dùng JavaScript production | PASS **9/9**, 0 fail/skip; `TestResults/qa3/qa3-browser.xml`. |
| Rà secret working tree | 0 finding; `TestResults/qa3/security-audit.csv`. Không quét lịch sử Git trong lượt này. |
| Dọn môi trường test | Sau lượt chạy không còn container mang label Testcontainers. Không reseed/sửa database hoặc audio đang dùng của đồ án. |

Các test .NET chạy Release trên SQL Server 2022 Testcontainers, demo seeder tắt,
không dùng biến kết nối SQL local. Dùng `tests/low-memory.runsettings` để chạy
tuần tự. Phân bố 229 integration case:

| Nhóm | Số case |
| --- | ---: |
| AssessmentAttemptStartTests | 22 |
| AssessmentLifecycleAcceptanceTests | 21 |
| AssessmentQueryServiceTests | 44 |
| AssessmentUiFlowTests | 21 |
| CourseCompletionIntegrationTests | 24 |
| LearningDashboardFeatureTests | 17 |
| PaidEnrollmentAssessmentAccessTests | 14 |
| PracticeQuestionValidationTests — filter retake/valid existing | 9 |
| TeacherAssessmentConfigurationTests | 23 |
| TeacherAssessmentPreviewTests | 18 |
| TeacherAssessmentScopeTests | 16 |

11 case trọng tâm nằm trong 229 case hồi quy; **không cộng thành 240 testcase**.
Không gộp lượt chạy này với baseline 1.120 integration của A1 thành tổng test mới.

Lần chạy browser đầu không mở được Chromium do máy thiếu binary đúng phiên bản,
chưa chạy tới assertion. Đã cài browser theo Playwright hiện có rồi chạy lại 9/9
pass; không sửa source/expected để né lỗi. Trình cài tự dọn browser cache cũ không
còn dùng; cache có thể tải lại, không phải dữ liệu dự án. Tải FFmpeg có lỗi DNS ở
mirror đầu, mirror dự phòng thành công. Không đổi package/version hoặc lockfile.

Browser suite là HTML fixture, không dùng account/SQL và không chứng minh UAT
toàn app. Case trang Expired gửi POST vẫn kiểm tra fallback cũ; lazy-finalize mới
được chứng minh bằng HTTP integration, không dựa vào JavaScript.

Source fingerprint SHA-256 lúc chạy:

- `AssessmentAttemptService.cs`: `C870D39212F89CA8857FEA32D4B94AC35D7D1CA4DE307FBBDE23E767F8FCCA58`.
- `AssessmentController.cs`: `8358FE68CDF21E9820185963F223AEF491643FFE273214B17E28EF9E8C0A940B`.
- `AssessmentAttemptStartTests.cs`: `2DFB36BDBD223605A4FBCF7D0BA2077CEB46A723ADF0264808F44DDC6B07DC2A`.
- `assessment-ui.test.mjs` không đổi: `D174AE502FEEE454DA9C5AE37A7E5EB44B4594E2C2195C3FBE399598722FD161`.

`TestResults/` được Git ignore; artifact chưa tự có trên máy thành viên khác.
Khi bàn giao cần gắn artifact đã rà secret với commit/CI cuối cùng.

## 4. Kết luận và giới hạn

- Có thể đánh dấu hoàn thành **A3 theo sáu mục Student đã nêu**.
- GAP-02 của [checklist A1](practice-assessment-a1-checklist.md) đã xử lý trên
  HTTP Student Take/History/Result. Màn hình/API Teacher đọc Attempt chưa được
  triển khai trong task này; không coi toàn bộ AC-SUB-09 phía Teacher đã hoàn thành.
- Chưa chạy lại UAT toàn web thủ công. UI-06–08 và bằng chứng demo thuộc QA5/QA6;
  không tự tick các dòng đó từ kết quả integration hoặc browser fixture.
- Chưa commit, push, tạo PR hoặc cập nhật main trong A3.
