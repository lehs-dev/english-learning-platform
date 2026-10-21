# QA4 — Quyền owner và quyền truy cập theo Enrollment

Ngày kiểm tra: **21/10/2026, Asia/Bangkok**.

**Kết luận: QA4 đạt. Source cuối pass 390 unit và 435 integration case; build
0 warning/error.** Đây là nghiệm thu tự động, chưa thay UAT thủ công QA5.

## 1. Phạm vi và thay đổi

QA4 kiểm tra Teacher owner, Student owner, Enrollment/thanh toán, audio nội bộ,
Independent và các request từ tab cũ. Không chỉ kiểm tra nút bị ẩn trên UI.
Tái sử dụng test đã có; chỉ thêm **2 method test, tổng 4 biến thể**, mở rộng
test audio thêm 3 biến thể và tăng assertion trong test Clone/Student khác.

Đã tìm thấy và sửa hai đường thiếu kiểm tra quyền:

| Vị trí | Trước khi sửa | Đã sửa và tác dụng |
| --- | --- | --- |
| [AssessmentController.cs](../../src/EnglishLearningPlatform.Web/Controllers/AssessmentController.cs) | Khi tab cũ mất Enrollment, query không trả taking sheet. Controller chuyển cả Save sang Finalize, có thể chấm bài sớm ngoài ý định. | Khi không có taking sheet, chỉ gọi `FinalizeExpiredAsync`: đã hết deadline mới chấm đáp án cũ; còn hạn thì từ chối, không nhận payload và không nộp sớm. |
| [AssessmentAttemptService.cs](../../src/EnglishLearningPlatform.Infrastructure/AssessmentAttempts/AssessmentAttemptService.cs) | SaveAnswer gọi trực tiếp chưa kiểm tra lại Enrollment. | Kiểm tra Enrollment/payment và owner Course bên trong transaction trước Save, dùng rule `Enrollments.Valid()` hiện có. Independent không cần Enrollment. Giữ nguyên contract lệnh `FinalizeAsync` chấm lịch sử của owner, không đổi nghiệp vụ completion cũ. |
| [StimulusAudioAccessService.cs](../../src/EnglishLearningPlatform.Infrastructure/Stimuli/StimulusAudioAccessService.cs) | Chỉ cần đã có Attempt hoặc PracticeSubmission là đọc được audio, kể cả khi Payment không còn hợp lệ/Enrollment đã bị xóa. | Lịch sử Course-linked chỉ cấp quyền audio khi vẫn có Enrollment hợp lệ và cùng owner Course; áp dụng cho cả Attempt và PracticeSubmission. Independent và Teacher sở hữu Stimulus vẫn đọc theo quyền tương ứng. |

Không sửa schema/migration, không sửa dữ liệu demo, không thay UI. Các thay đổi
QA2/A3 đang có được giữ nguyên. Mốc gốc là
`3f45ac1501c05db4b7a6b70975c37c1add05e8c2`, cộng working tree QA2–QA4 chưa commit.

Fixture negative thay trạng thái Payment/Enrollment trong **DB test** để mô
phỏng quyền đổi trong lúc tab đang mở. Đối chứng thanh toán thật qua callback
hợp lệ vẫn được kiểm bằng `PaidEnrollmentAssessmentAccessTests`, không coi
việc tự tạo Payment trong fixture là chứng minh xác minh thanh toán.

Phân biệt quyền hiện tại với lịch sử:

- Rút/Archive đề hoặc Course **không** tự làm mất Enrollment. Student còn quyền
  được hoàn thành lượt đang làm theo A3; audio lịch sử vẫn đọc được.
- Mất Enrollment/payment hợp lệ: không Resume/Start/Save hoặc Submit sớm từ tab cũ,
  không dùng Attempt cũ để vượt quyền đọc audio Course-linked.
- History/Result tổng hợp của chính Student vẫn được xem sau khi mất Enrollment.
  Chấm tự động khi đã hết deadline vẫn theo rule server, không nhận đáp án mới.
  Không dùng việc mất Enrollment để kéo dài deadline hoặc xóa lịch sử.
- `FinalizeAsync` nội bộ vẫn được chấm từ đáp án đã lưu của owner theo nghiệp vụ
  lịch sử cũ, nhưng không cấp completion khi Enrollment sai. HTTP Save/Submit
  không có taking sheet không còn gọi lệnh chấm không giới hạn này; chúng chỉ
  được gọi đường hết hạn có kiểm tra đồng hồ server.

## 2. Testcase và đối chứng

| Mã | Điều kiện / thao tác | Kết quả mong đợi | Test tái sử dụng hoặc bổ sung |
| --- | --- | --- | --- |
| QA4-01 | Teacher B dùng URL/form của Assessment Draft A để list/read/update; gửi owner giả. | Không thấy/sửa được đề A; quyền lấy từ tài khoản, không từ form; dữ liệu A không đổi. | `TeacherAssessmentDraftTests.OtherTeacher_CannotListReadOrUpdateOwnersDraft`; `Http_GuestStudentForeignOwnerAndMissingCsrf_AreRejectedWithoutWrites`. |
| QA4-02 | Teacher B đọc/sửa câu hỏi trong đề, Preview và Publish đề A qua service/HTTP với CSRF hợp lệ. | Từ chối, không sửa version/câu/points/status/dấu khóa hoặc tạo Attempt. | `TeacherAssessmentQuestionsTests.ForeignOrUnauthorizedActor_CannotReadOrSave`; `Http_GuestForeignOwnerAndMissingCsrf_CannotReadOrModifyDraft`; `TeacherAssessmentPreviewTests.Service_RejectsForeignOrUnauthorizedActor`; `TeacherAssessmentPublishTests.Publish_ForeignOrUnauthorizedActor_IsRejected`; `Http_GetAndForeignTeacher_CannotPublish`. |
| QA4-03 | Teacher B POST Clone đề A, có CSRF và version đúng, gửi owner giả. | 404; không thêm Assessment; snapshot đề gốc không đổi. Owner thật vẫn Clone được thành Draft. | Mở rộng `AssessmentCloneTests.Clone_DeniesStaleVersionForeignOwnerStudentAndMissingCsrf_HttpRedirectsToNewDraft`. |
| QA4-04 | Teacher B đọc/sửa Practice hoặc Question của A; dùng Stimulus khác owner. | Không xem/sửa nội dung không thuộc mình hoặc gắn Stimulus sai owner; không thay dữ liệu cũ. | `TeacherPracticeQuestionsTests.Service_ForeignOrNonTeacher_CannotReadOrWritePractice`; `TeacherQuestionBankAuthoringTests.Teacher_CannotEditQuestion_OwnedByAnotherTeacher`; `Teacher_CreateQuestion_WithForeignStimulus_FailsValidation`; các test Query Detail/Search. |
| QA4-05 | Student B biết AttemptId của A, thử Take/Result/History và Save/Submit có CSRF của B. | Không đọc/sửa/chấm bài A, kể cả InProgress; snapshot Answer/SkillResult/điểm/deadline/timestamp không đổi. | Mở rộng `AssessmentUiFlowTests.ForeignAttempt_ResultReadAndSaveSubmitDenied_AndResultSurvivesWithdrawal`; tái sử dụng `History_ThreeWeightedAttemptsRemainSeparateAfterWithdrawalAndEnrollmentRevocation` và test lazy-finalize A3. |
| QA4-06 | Chưa Enrollment hoặc Payment Pending/Failed/Cancelled/callback sai chữ ký; truy cập trực tiếp Course-linked. | Không xem/Start được; không sinh Enrollment/Attempt; free/paid Enrollment hợp lệ là đối chứng được phép. | `PaidEnrollmentAssessmentAccessTests.PaymentWithoutVerifiedSuccess_DeniesEnrollmentLearningAndAssessment`; `AssessmentQueryServiceTests.OverviewAndTakingSheet_AllowValidFreeAndPaidEnrollment`; `AssessmentUiFlowTests.GetOverview_DoesNotConsumeAttempts_AndStartRequiresStudentEnrollmentAndCsrf`. |
| QA4-07 | Đã Start/lưu 4 câu đúng, mở tab hợp lệ; sau đó Enrollment mất hoặc Payment Pending/Failed. Dùng URL, CSRF và payload cũ để Start/Save/Submit, rồi gọi service ghi trực tiếp. | Bị chặn; vẫn một Attempt InProgress, bộ Answer/điểm/deadline/timestamp cũ không đổi; không thêm SkillResult. | **Mới:** `AssessmentUiFlowTests.StaleTakingTab_InvalidEnrollment_BlocksStartSaveAndEarlySubmitWithoutWrites` — 3 biến thể. Tái sử dụng test paid gồm 7 biến thể sai status/buyer/course/amount/currency. |
| QA4-08 | GET audio nội bộ khi chưa enroll, Pending/Failed/Cancelled hoặc Succeeded; Teacher owner/khác owner đối chứng. Đã có Attempt/PracticeSubmission rồi mất Payment/Enrollment. | Chỉ quyền hợp lệ đọc được; biết hash/có lịch sử không vượt quyền. Course Archive nhưng Enrollment còn hợp lệ vẫn đọc được. Request bị chặn không chấm hoặc đổi deadline. | Mở rộng `StimulusCreationTests.ManagedAudio_CourseScopeHonorsPaymentAndOwnership` từ 3 thành 6 biến thể; kiểm cả nhánh lịch sử Practice dùng chung audio. |
| QA4-09 | Student mở Independent Published, Teacher gắn Course trước lúc Student bấm Start; sau đó gỡ về Independent. | Tab cũ bị chặn không tạo Attempt; gỡ về Independent thì Student không Enrollment vẫn xem/Start được. Định nghĩa/version không đổi do request bị chặn. | **Mới:** `TeacherAssessmentScopeTests.Http_StaleIndependentStart_RechecksCourseScope_AndDetachRestoresIndependentAccess`. Tái sử dụng `ScopeTransition_ImmediatelyChangesStudentOverviewAndStartEnrollmentRequirements` và race Start/scope 2 thứ tự. |
| QA4-10 | Editor/version cũ hoặc cookie của tài khoản bị khóa/đổi role; gửi request trực tiếp. | Không ghi đè cấu hình mới; phiên cũ không vượt quyền server. | Test stale Draft/Questions/Publish/Scope/Clone có sẵn; `AuthorizationIntegrityAcceptanceTests.InactiveAccount_RejectsExistingCookieImmediately_ForApiAndMvc`; `ChangedRole_RejectsStaleCookieImmediately_WithoutSecurityStampChange`. |

Các testcase dùng test host HTTP/MVC thật và SQL Server riêng. 404 dùng để che
resource không thuộc actor; Forbid ở MVC hiện chuyển hướng AccessDenied. Cookie
API có test riêng cho 401/403; không đánh đồng mọi redirect với HTTP 403.

## 3. Lượt chạy và bằng chứng

| Lượt chạy | Kết quả / artifact local |
| --- | --- |
| Build solution Release | PASS: 0 warning, 0 error. |
| Unit bản thử | PASS 390/390, 0 fail/skip; 18:15:03–18:15:04 (+07:00); `TestResults/qa4/qa4-unit.trx`. Chưa dùng lượt này làm bằng chứng bản sửa hẹp cuối. |
| Tái hiện trước sửa | `TestResults/qa4/qa4-before-fix.trx`: 6/10 pass; 3 lỗi setup fixture Payment và 1 lỗi quyền audio. Đã thêm `db.Payments.Add` trong test để fixture tạo đúng Payment, không sửa production để né FK. |
| Xác nhận lỗi production sau khi sửa fixture | `TestResults/qa4/qa4-confirmed-red.trx`: 4/8 pass; 3 biến thể Save từ tab cũ chuyển sang Result thay vì từ chối, 1 case audio vẫn trả 200 sau Payment Pending. Giữ report fail gốc. |
| Trọng tâm bản thử | PASS 12/12, 0 fail/skip; 18:13:47–18:14:41; `TestResults/qa4/qa4-focus.trx`. |
| Hồi quy lần đầu | `TestResults/qa4/qa4-regression.trx`: 427/435 pass, 8 fail; 6 case audio bị ảnh hưởng dữ liệu chung, 2 case completion phát hiện guard Finalize quá rộng trong bản sửa thử. Chi tiết bên dưới; không xóa report fail. |
| Xác nhận fixture và bản thử | PASS 25/25, 0 fail/skip; 18:20:48–18:21:28; `TestResults/qa4/qa4-fix-validation.trx`. Lượt này còn guard Finalize và test completion bản thử, không phải bản cuối. |
| Hồi quy bản thử | PASS 435/435; `TestResults/qa4/qa4-regression-final.trx`. Sau đó đã thu hẹp fix sang HTTP và khôi phục contract/test completion gốc, nên không gán lượt này cho source cuối. |
| Unit source cuối | PASS 390/390, 0 fail/skip; 18:27:31–18:27:32 (+07:00); `TestResults/qa4/qa4-unit-verified.trx`. |
| Hồi quy source cuối | PASS **435/435**, 0 fail/skip; 18:27:33–18:32:13 (+07:00); `TestResults/qa4/qa4-regression-verified.trx`. |
| Rà secret working tree | 0 finding; `TestResults/qa4/security-audit.csv`. Không quét lịch sử Git trong lượt này. |
| Môi trường test | Container và kho audio test tự dọn; không sửa database/audio người dùng. |

Integration dùng SQL Server 2022 Testcontainers và `tests/low-memory.runsettings`,
chạy tuần tự; `LearningDemo__Enabled=false`, bỏ biến kết nối SQL local.
Không dùng hoặc reseed database/audio đang chạy của đồ án. Không xóa kết quả
fail cũ hoặc đổi expected cho xanh.

Hai vấn đề phát hiện khi chạy rộng hơn:

- Audio được lưu theo hash. File WAV chung của test đã có trong một Assessment
  Independent khác; Student có nguồn cấp quyền hợp lệ tới cùng bytes nên GET
  trả 200 là đúng. Fixture audio negative nay dùng PCM ngẫu nhiên riêng mỗi ca,
  vẫn WAV hợp lệ, để chỉ đo quyền của Course đang kiểm tra. Không đổi rule dedup
  hoặc chặn một tài nguyên mà Student có quyền qua nội dung khác.
- `CourseCompletionIntegrationTests.PaymentInvalidatedBeforeFinalize_DoesNotRecordCourseCompletion`
  bảo vệ contract nội bộ đã được mô tả trong tài liệu nghiệm thu cũ: chấm lịch sử
  từ đáp án cũ nhưng không cấp completion khi Payment sai. Bản sửa thử chặn cả
  `FinalizeAsync` là quá rộng. Đã bỏ guard đó, giữ nguyên source/expected test
  completion gốc và chỉ hạn chế đường fallback HTTP bằng `FinalizeExpiredAsync`.
  Không đổi expected của test cũ để ép hồi quy pass.

12 ca trọng tâm cũng nằm trong filter hồi quy, không cộng hai lượt thành số
testcase mới. Không cộng các lượt QA2/A3/baseline vào số test QA4. Artifact nằm
trong `TestResults/` được Git ignore, chưa phải CI hoặc bằng chứng UAT toàn web.

Lượt cuối gồm 22 nhóm test: owner Draft/Questions/Preview/Publish/Scope/Clone,
Practice/Question Bank, Role/cookie cũ, Enrollment/thanh toán và audio; đồng thời
chạy lại Start, deadline/Resume, Result/history, scoring/completion và dashboard.
Source/expected `CourseCompletionIntegrationTests` cuối cùng không đổi so với
trước QA4. Tên testcase trong báo cáo đã được đối chiếu source và nhóm hồi quy.

Fingerprint SHA-256 production đã kiểm tra:

- `AssessmentAttemptService.cs`: `DFC18EAD20A96D1ACEB33F3ADA4B149395C2172C18EE745EB3594691415D47EF`.
- `AssessmentController.cs`: `4D59CC2B985F8733BE03E414325154442C6652A18A73603D6DD9E0B4D6FFD34A`.
- `StimulusAudioAccessService.cs`: `CDD9E60D79630EAC4596D91FADB13AADE1268A1DABC94CBF33B7069DBA060CFD`.

Fingerprint file audio test đã tách dữ liệu sau hồi quy đầu:

- `StimulusCreationTests.cs`: `1EC8D7C2169E733E6B773832A2A72BBF7A98AE4E61649FDEDFCC0EC0E852FA21`.

## 4. Kết luận

**Có thể đánh dấu hoàn thành QA4 theo phạm vi đã nêu.** Các đường bị từ chối đã
có đối chứng snapshot/count; Independent và Enrollment hợp lệ vẫn hoạt động,
không phá deadline hoặc lịch sử. Không tự tick UI-05/UI-10 hoặc QA5/QA6 từ kết
quả test tự động. Chưa commit/push, tạo PR, cập nhật main hoặc sửa database demo.

Quyền audio được kiểm tra trên request mới. Bytes đã tải hợp lệ trước khi mất
quyền không thể bị thu hồi khỏi browser; khi UAT phải thử URL/request mới, không
coi audio đang nằm trong buffer là request vượt quyền.
