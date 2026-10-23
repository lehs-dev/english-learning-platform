# A1 — Checklist kiểm thử Practice–Assessment–Result

Ngày đối chiếu: **21/10/2026, Asia/Bangkok**.

Cập nhật QA6: đã có [bộ bàn giao và actual tổng hợp](qa6-handover-results.md),
CSV kết quả từng test từ TRX, ZIP evidence và [runbook/demo](../handover/qa6-run-demo.md).
Giữ nguyên các kết quả lịch sử bên dưới; rehearsal thủ công của nhóm chưa tự được ký nhận.
Lượt chốt sau cập nhật main: [pre-push QA2–QA6](qa-pre-push-20261021.md),
390 unit + 1.143 integration + 9 fixture + 6 live browser pass; không cộng các lượt chạy lặp.

## 1. Phạm vi và cách đọc

Checklist này phục vụ nhóm việc: chấm điểm, Start/Resume/deadline, quyền owner,
Enrollment/thanh toán, không tạo kết quả trùng và UAT Practice–Assessment–Result.
Các luồng Teacher, passage/audio, Course scope, Final và Clone được đưa vào khi
chúng ảnh hưởng trực tiếp tới hành trình này. Đây không phải nghiệm thu toàn bộ
Admin, analytics hay toàn bộ đồ án.

Nguồn yêu cầu: tài liệu `Project.md` đã cung cấp và các task đã chốt trong hội thoại.
Đối chiếu chính: BR05–BR19, TBR01, TBR03, TBR05–TBR08, TBR11–TBR18;
AC-PRC, AC-ATT, AC-RSM, AC-SUB, AC-SCR, AC-SKL, AC-PRA, AC-ASM, AC-AVL,
AC-LNK, AC-VER và AC-FIN.

| Trạng thái | Ý nghĩa |
| --- | --- |
| PASS-AUTO | Có test nguồn, có kết quả `Passed` trong TRX baseline hoặc lượt QA2/A3/QA4 được dẫn chiếu. Chỉ xác nhận các điều kiện test thực sự assert. |
| CHỜ-UI | Chưa có biên bản UAT thủ công mở rộng/nhóm của bản code đang đối chiếu. QA5 automated browser UAT được dẫn riêng, không suy ra UI pass từ unit/integration. |
| THIẾU-TEST | Chưa tìm thấy test trực tiếp chứng minh tình huống được ghi rõ. Chưa kết luận chức năng sai. |
| LỆCH-RULE | Hành vi hiện tại chưa đáp ứng đầy đủ yêu cầu tài liệu; không đồng nghĩa test suite đang fail. |
| CHỜ-BẰNG-CHỨNG | Có script/tài liệu hoặc kết quả cũ, nhưng chưa đủ bằng chứng nghiệm thu bản hiện tại. |

Trong phần 3, mọi dòng là **test tự động**. Phần 4 là **UI thủ công**.
`AssessmentUiFlowTests` kiểm tra HTTP/MVC qua test host, **không chạy browser/JavaScript thật**.
Script Playwright chạy browser thật được ghi riêng ở phần 5.

## 2. Baseline đã xác minh

| Thông tin | Giá trị |
| --- | --- |
| Nhánh | `codex/stimulus-flow-completion` |
| Mốc code đối chiếu | `3f45ac1501c05db4b7a6b70975c37c1add05e8c2` |
| Unit | 390 tổng / 390 pass / 0 fail / 0 bỏ qua; kết thúc 12:17:44 ngày 21/10/2026 (+07:00). |
| Integration | 1.120 tổng / 1.120 pass / 0 fail / 0 bỏ qua; kết thúc 12:27:49 ngày 21/10/2026 (+07:00). |
| Bằng chứng local | `TestResults/pre-push-unit.trx`, `TestResults/pre-push-integration.trx`. |
| Security audit sau commit | `TestResults/pre-push-security-audit.txt`: candidate đúng SHA trên, `findings=0`. |
| Cách lập A1 | Đọc yêu cầu, test, code liên quan và báo cáo có sẵn; không chạy lại suite, không thêm test hay sửa chức năng. |

Hai TRX được tạo trong lần kiểm tra trước commit/push của cùng bộ runtime vừa
đóng gói thành commit trên. Bản thân TRX không nhúng SHA; liên hệ candidate dựa
trên nhật ký lượt pre-push và audit sau commit, không phải xác nhận CI từ GitHub.
Ở bước lập A1, thay đổi chỉ là tài liệu. QA2 sau đó bổ sung một test Practice;
kết quả mới và source fingerprint nằm tại [báo cáo QA2](qa2-scoring-results.md).
QA2 chạy lại 8 unit và 113 integration case liên quan, không thay baseline toàn suite.
Các thay đổi deadline/lazy-finalize và lượt chạy mới được ghi riêng tại
[báo cáo A3](qa3-timeout-resume-results.md); không gán kết quả baseline cho code A3.
Quyền từ tab cũ và audio được rà/bổ sung ở [báo cáo QA4](qa4-owner-enrollment-results.md).
Giữ report QA2/A3 là bằng chứng của từng lượt chạy, không thay SHA/fingerprint cũ.
QA4 source cuối: 390 unit và 435 integration case pass; không cộng các lượt
chạy chồng lặp thành số testcase mới và không gọi kết quả local là CI.
QA5 bổ sung Preview Practice theo lựa chọn người dùng và chạy UAT live browser:
6/6 kịch bản, 48 ảnh, 0 lỗi JavaScript; 390 unit + 151 integration pass.
Xem [báo cáo QA5](qa5-journey-results.md) và digest source cuối tại đó. Không gán
kết quả QA4 cho code Preview mới hoặc coi 13 dòng rehearsal mở rộng dưới đây đều đã chạy.

Tổng 1.510 lượt test bao gồm cả module ngoài phạm vi A1. Không coi 1.510 là số
testcase riêng của Assessment; không cộng lại các test được dẫn chiếu nhiều lần
ở các dòng dưới. Một `[Theory]` có thể tạo nhiều lượt test.

`TestResults/` được Git ignore. Các TRX là bằng chứng local, không tự có trên máy
thành viên khác. A6 cần đính kèm artifact đã rà secret cùng SHA/build/CI tương ứng;
không đưa nguyên log, cookie, mật khẩu hoặc connection string vào báo cáo.

## 3. Test tự động hiện có — tái sử dụng, không viết lại

### 3.1. Chấm điểm và kết quả — chuyển tiếp A2

| Mã | Điều kiện ban đầu | Thao tác | Kết quả mong đợi | Kết quả thực tế | Test hiện có |
| --- | --- | --- | --- | --- | --- |
| SC-01 | Hai Assessment dùng cùng hai Question, nhưng points đảo nhau 1/3 và 3/1. | Chấm cùng lựa chọn đúng câu đầu, sai câu sau. | Điểm lần lượt 25% và 75%; Question Bank không bị sửa. | PASS-AUTO: 25%/75%, nội dung/Option không đổi. | [Scoring](../../tests/EnglishLearningPlatform.UnitTests/ExamScoringServiceTests.cs): `ExamScoringServiceTests.Score_SharedQuestions_UsePointsOfEachAssessmentWithoutChangingQuestionBank`. |
| SC-02 | Đề có points 1,25; 2,5; 0,75; 3; có câu đúng, sai, bỏ chọn và chưa trả lời. | Chấm bài. | Chỉ nhận 1,25/7,5 điểm; câu sai/trống nhận 0, không trừ điểm; hiển thị 16,67%. | PASS-AUTO: khớp điểm thô và phần trăm. | [Scoring](../../tests/EnglishLearningPlatform.UnitTests/ExamScoringServiceTests.cs): `ExamScoringServiceTests.Score_FractionalPointsAndBlankAnswers_CountEveryQuestionInMaximumScore`. |
| SC-03 | Assessment có hai skill, mỗi skill tối đa 5 điểm; tổng đề 10 điểm. | Lưu đúng/sai/bỏ trống rồi Finalize bằng service thật. | Overall 50%; Grammar 25%; Vocabulary 75% trong DB; không lấy mẫu số 10 cho từng skill. | PASS-AUTO: đọc lại DB đúng điểm và số câu của từng nhóm. Vocabulary 2 câu vẫn được lưu dù bị lọc khỏi breakdown Student. | [Query](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentQueryServiceTests.cs): `AssessmentQueryServiceTests.FinalizedAttempt_FractionalPointsAndBlanks_PersistSeparateSkillDenominators`. |
| SC-04 | Final có raw score 69,996%, PassingScore 70%; mọi Lesson đã hoàn thành. | Chấm Final và kiểm tra completion. | Hiển thị 70% nhưng Fail; không ghi CompletedAt vì raw score chưa đạt. | PASS-AUTO: `Passed=false`, CompletedAt vẫn null. | [Completion](../../tests/EnglishLearningPlatform.IntegrationTests/CourseCompletionIntegrationTests.cs): `CourseCompletionIntegrationTests.RoundedDisplayScoreAtPassingBoundary_DoesNotCompleteFailedFinal`. |
| SC-05 | Skill đủ 3 câu; điểm thô 59,996%, đúng 60% hoặc 0%; PassingScore của đề là 80%. | Đọc kết quả sau chấm. | Điểm yếu dùng ngưỡng raw <60%, độc lập PassingScore; 0% có dữ liệu khác với chưa được đánh giá. | PASS-AUTO: 3 biến thể đều đúng; nhóm thiếu câu không bị gán thành 0% trên giao diện kết quả. | [Query](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentQueryServiceTests.cs): `AssessmentQueryServiceTests.Result_WeakSkillUsesRawPoints_NotRoundedScoreOrPassingScore`. |
| SC-06 | Đề 3 Reading + 2 Listening; biến thể 2 Reading + 2 Listening + 1 Grammar. | Submit rồi đọc DTO và HTML Result nhiều lần. | Chỉ Reading đủ 3 câu được hiện; biến thể thứ hai báo chưa đủ dữ liệu. Không lộ đáp án đúng/đã chọn/giải thích; không xóa SkillResult. | PASS-AUTO: 2 biến thể; dữ liệu SkillResult và Answer sau đọc vẫn giữ nguyên. | [HTTP](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentUiFlowTests.cs): `AssessmentUiFlowTests.Result_OnlyShowsSkillsWithThreeQuestions_WithoutExposingOrDeletingAnswers`. |
| SC-07 | Placement không có PassingScore; MaxAttempts=1. | Submit bài 0%, sau đó Start lại. | Không tự gán Fail khi không có PassingScore; hết lượt vẫn có đường xem kết quả cũ. | PASS-AUTO cho không hiển thị nhãn Fail, giữ link Result và không tạo lượt thứ hai. Không dùng case này để suy ra mọi nhãn CEFR đã được browser kiểm tra. | [HTTP](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentUiFlowTests.cs): `AssessmentUiFlowTests.PlacementResultWithoutPassingScore_DoesNotLabelStudentFailed_AndExhaustedAttemptsRetainResultLink`. |
| SC-08 | Student có ba lượt đã chấm 60%, 85%, 70%. | Xem history; rút đề và thu hồi Enrollment rồi xem lại history của chính mình. | Ba lượt và kết quả riêng không bị ghi đè; không mở lại quyền Start mới. | PASS-AUTO: giữ đủ từng lượt và đường Result lịch sử. | [HTTP](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentUiFlowTests.cs): `AssessmentUiFlowTests.History_ThreeWeightedAttemptsRemainSeparateAfterWithdrawalAndEnrollmentRevocation`. |
| SC-09 | Ba lượt 60%, 85%, 70%; Skill của các lượt khác nhau. | Đọc Dashboard và Profile. | Đại diện là 85%; Overall và Skill lấy từ cùng lượt 85%, không chọn lượt mới nhất hoặc ghép best từng skill. | PASS-AUTO: backend và HTML Dashboard/Profile nhất quán. | [Dashboard](../../tests/EnglishLearningPlatform.IntegrationTests/LearningDashboardFeatureTests.cs): `LearningDashboardFeatureTests.DashboardAndProfile_KeepHighest85AndItsSkills_WhenLatestAttemptScores70`. |
| SC-10 | Nhiều lượt đồng Overall; có lượt bằng FinalizedAt, lượt chưa chấm và metadata chấm thiếu. | Đọc điểm đại diện. | Chọn highest, rồi FinalizedAt mới nhất, rồi Id lớn hơn; loại lượt chưa chấm/không đủ metadata. | PASS-AUTO: chọn deterministic; Skill đi cùng Attempt đã chọn. | [Dashboard](../../tests/EnglishLearningPlatform.IntegrationTests/LearningDashboardFeatureTests.cs): `LearningDashboardFeatureTests.Dashboard_UsesRepresentativeAttemptWithSameSkillDataAndDeterministicTies`. |
| SC-11 | Student chưa có bằng chứng điểm đã chấm. | Đọc Dashboard. | Không tự tạo 0% hoặc kết luận năng lực; không lấy dữ liệu Student khác. | PASS-AUTO: không phát minh điểm/recommendation. | [Dashboard](../../tests/EnglishLearningPlatform.IntegrationTests/LearningDashboardFeatureTests.cs): `LearningDashboardFeatureTests.Dashboard_WithoutEvidenceDoesNotInventScoresOrRecommendations_AndScopesToStudent`. |
| SC-12 | Practice có 3 câu; 1 đúng, 1 sai, 1 bỏ trống; sau đó làm lại đúng cả 3. | Submit bằng service thật, đọc feedback và DB rồi retake bằng scope mới. | 33,33% → 100%; cùng SubmissionId; số Answer từ 2 thành 3, không giữ lựa chọn cũ. | PASS-AUTO trong lượt QA2 trên SQL local và Docker; GAP-01 đã đóng. | [Dashboard/Practice](../../tests/EnglishLearningPlatform.IntegrationTests/LearningDashboardFeatureTests.cs): `LearningDashboardFeatureTests.Practice_MixedCorrectWrongAndBlankAnswers_UseEqualWeightsAndRetakeReplacesLatestResult`. |

### 3.2. Start, lưu đáp án, Resume và timeout — chuyển tiếp A3

| Mã | Điều kiện ban đầu | Thao tác | Kết quả mong đợi | Kết quả thực tế | Test hiện có |
| --- | --- | --- | --- | --- | --- |
| DL-01 | Student mở overview nhưng chưa Start. | GET/F5; thử Start thiếu Enrollment hoặc CSRF. | GET không tiêu thụ lượt; Start không hợp lệ bị chặn. | PASS-AUTO qua HTTP; không tạo Attempt chỉ vì mở trang. | [HTTP](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentUiFlowTests.cs): `AssessmentUiFlowTests.GetOverview_DoesNotConsumeAttempts_AndStartRequiresStudentEnrollmentAndCsrf`. |
| DL-02 | Attempt còn hạn, đã lưu một số đáp án. | GET nhiều lần; đăng nhập lại rồi Resume. | Cùng AttemptId, cùng đáp án đã lưu và deadline ban đầu; không có lượt mới. | PASS-AUTO qua HTTP, không chứng minh timer/JavaScript bằng mắt. | [HTTP](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentUiFlowTests.cs): `AssessmentUiFlowTests.StartResume_NewLoginAndRepeatedGets_KeepOneAttemptSavedAnswersAndOriginalDeadline`. |
| DL-03 | MaxAttempts đã đạt nhưng có Attempt đang làm, hoặc Attempt đó vừa hết hạn. | Start lại. | Còn hạn thì Resume; hết hạn thì chấm lượt cũ; không tạo lượt vượt giới hạn. | PASS-AUTO: 2 biến thể. | [Start](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentAttemptStartTests.cs): `AssessmentAttemptStartTests.ActiveAttempt_AtMaxAttempts_ReturnsExistingAttempt`. |
| DL-04 | Đã hoàn tất lượt cuối cùng được phép. | Start thêm. | Trả hết lượt và không thêm Attempt. | PASS-AUTO. | [Start](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentAttemptStartTests.cs): `AssessmentAttemptStartTests.FinalizedAttempt_ConsumesMaxAttempts`. |
| DL-05 | Query dùng clock cố định; deadline trước, đúng hoặc sau now 1 giây. | Đọc taking sheet. | Chỉ còn hạn mới có sheet; đúng deadline bị chặn. | PASS-AUTO cho đường query, 3 biến thể. Query vẫn chỉ đọc; HTTP gọi write service riêng để lazy-finalize, xem DL-11/12. | [Query](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentQueryServiceTests.cs): `AssessmentQueryServiceTests.TakingSheet_EnforcesServerDeadlineWithoutFinalizing`. |
| DL-06 | Đã lưu 80%; deadline đã qua; request muộn cố đổi đáp án thành 100%. | Gửi Save hoặc Submit rồi gửi lặp. | Chấm 80% từ đáp án hợp lệ cũ, lý do hết giờ; không ghi đáp án muộn hoặc đổi thời điểm chấm. | PASS-AUTO: cả Save và Submit, SkillResult không bị nhân đôi. | [HTTP](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentUiFlowTests.cs): `AssessmentUiFlowTests.ExpiredPost_GradesSavedAnswersOnceAndIgnoresLateChanges`. |
| DL-07 | Attempt đã có trước khi Assessment Unpublish/Archive hoặc Course Archive. | Resume/Finalize khi còn hạn hoặc đã hết hạn. | Giữ deadline và quyền hoàn tất lượt cũ; không mở Start mới trên nội dung đã rút. | PASS-AUTO cho các biến thể lifecycle. | [Lifecycle](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentLifecycleAcceptanceTests.cs): `AssessmentLifecycleAcceptanceTests.ExistingAttempt_ResumesOrFinalizesAfterAssessmentWithdrawn`; `AssessmentLifecycleAcceptanceTests.ExistingCourseAttempt_PreservesDeadlineAndCanFinishAfterArchive`. |
| DL-08 | Attempt còn hạn; các câu đã có lựa chọn. | Gửi JSON đổi, retry và bỏ chọn một câu; tải lại. | Mỗi câu có tối đa một Answer; giữ các câu khác; khôi phục lựa chọn mới nhất đã lưu. | PASS-AUTO qua HTTP/DB. Thứ tự autosave phía browser được kiểm riêng ở phần 5. | [HTTP](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentUiFlowTests.cs): `AssessmentUiFlowTests.SaveJson_ChangeRetryAndClear_KeepOneAnswerPerQuestionAndPreserveOtherChoices`. |
| DL-09 | Đã lưu một đáp án đúng; clock server ở deadline -1 tick, đúng deadline hoặc +1 tick. | Gửi đổi sang đáp án sai, sau đó finalize và gửi lặp. | Trước hạn được lưu; đúng/sau hạn không được đổi. Giữ deadline gốc; chấm một lần, không đổi timestamp khi lặp. | PASS-AUTO A3: 3 biến thể; trước hạn 0%, đúng/sau hạn 20% từ đáp án đúng cũ. | [Start](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentAttemptStartTests.cs): `AssessmentAttemptStartTests.Save_OneTickBeforeAtAndAfterDeadline_UsesServerClockAndPreservesOriginalDeadline`. |
| DL-10 | Đã lưu một đáp án đúng; clock đúng deadline. | Submit và lazy-finalize chạy đồng thời bằng hai DbContext. | Một Graded và một AlreadyGraded; cùng 20%, DeadlineElapsed; một bộ Answer/SkillResult, deadline không đổi. | PASS-AUTO A3. | [Start](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentAttemptStartTests.cs): `AssessmentAttemptStartTests.ManualSubmitAndLazyExpiry_AtExactDeadline_GradeOnceWithoutExtendingTime`. |
| DL-11 | Lượt hết hạn đã lưu 80%; đề/Course còn hoạt động hoặc đã Archive. | GET Take có/không attemptId, không gửi POST; sau đó replay Submit. | Chuyển sang Result đã chấm 80%; giữ timestamp và chỉ một Attempt; Submit thủ công vẫn cần CSRF. | PASS-AUTO A3: 4 biến thể. | [HTTP](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentUiFlowTests.cs): `AssessmentUiFlowTests.ExpiredGet_LazilyGradesSavedAnswersWithoutPost_AndReplayKeepsResult`. |
| DL-12 | Lượt của mình và Student khác còn hạn, sau đó đều hết hạn. | Đọc Result/History trước hạn, URL người khác, rồi URL của mình và F5. | Không nộp sớm/không chấm bài người khác; lượt của mình hết hạn được chấm 80%, giữ deadline/timestamp; query thuần vẫn read-only. | PASS-AUTO A3: 2 biến thể HTTP và một test query/HTTP bảo toàn các hàng khác. | [HTTP](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentUiFlowTests.cs): `AssessmentUiFlowTests.ReadPages_LazilyGradeOnlyOwnExpiredAttempt_AndNeverSubmitAnActiveAttempt`; [Query](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentQueryServiceTests.cs): `AssessmentQueryServiceTests.History_QueryIsReadOnly_HttpFinalizesExpiredButPreservesOtherRows`. |

### 3.3. Owner, role và Enrollment — chuyển tiếp A4

| Mã | Điều kiện ban đầu | Thao tác | Kết quả mong đợi | Kết quả thực tế | Test hiện có |
| --- | --- | --- | --- | --- | --- |
| AC-01 | Teacher B biết Id Draft của Teacher A. | List/read/update Draft bằng service hoặc URL/form. | Không đọc/sửa đề của A; owner lấy từ tài khoản, không từ form. | PASS-AUTO cho service và HTTP đối chứng. | [Draft](../../tests/EnglishLearningPlatform.IntegrationTests/TeacherAssessmentDraftTests.cs): `TeacherAssessmentDraftTests.OtherTeacher_CannotListReadOrUpdateOwnersDraft`; `TeacherAssessmentDraftTests.Http_GuestStudentForeignOwnerAndMissingCsrf_AreRejectedWithoutWrites`. |
| AC-02 | Teacher khác hoặc actor không được phép biết Id đề. | Thử Preview và Publish. | Từ chối; không đổi trạng thái hoặc tạo lịch sử. | PASS-AUTO. | [Preview](../../tests/EnglishLearningPlatform.IntegrationTests/TeacherAssessmentPreviewTests.cs): `TeacherAssessmentPreviewTests.Service_RejectsForeignOrUnauthorizedActor`; [Publish](../../tests/EnglishLearningPlatform.IntegrationTests/TeacherAssessmentPublishTests.cs): `TeacherAssessmentPublishTests.Publish_ForeignOrUnauthorizedActor_IsRejected`. |
| AC-03 | Practice thuộc Teacher A; actor Teacher B/Student/Admin. | Đọc editor, lưu câu hỏi, Publish/Unpublish bằng service. | Từ chối và giữ Practice cũ. | PASS-AUTO. | [Practice authoring](../../tests/EnglishLearningPlatform.IntegrationTests/TeacherPracticeQuestionsTests.cs): `TeacherPracticeQuestionsTests.Service_ForeignOrNonTeacher_CannotReadOrWritePractice`. |
| AC-04 | Teacher A nhận StimulusId của Teacher B khi tạo Question. | Submit form Question. | Báo lỗi, không gắn Stimulus khác owner. | PASS-AUTO qua HTTP. | [Question authoring](../../tests/EnglishLearningPlatform.IntegrationTests/TeacherQuestionBankAuthoringTests.cs): `TeacherQuestionBankAuthoringTests.Teacher_CreateQuestion_WithForeignStimulus_FailsValidation`. |
| AC-05 | Student B biết AttemptId đang làm hoặc đã chấm của Student A. | Đọc Result/Take, Save hoặc Submit trực tiếp với CSRF của B. | Không xem/sửa bài của A; kết quả A không đổi. | PASS-AUTO; QA4 bổ sung snapshot đối chứng lúc InProgress, không chỉ bài đã chấm. Đường truy cập bị che bằng 404 khi phù hợp. | [HTTP](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentUiFlowTests.cs): `AssessmentUiFlowTests.ForeignAttempt_ResultReadAndSaveSubmitDenied_AndResultSurvivesWithdrawal`. |
| AC-06 | Khóa trả phí; Payment pending, failed, cancelled hoặc callback chưa được xác minh. | Đọc overview và Start Assessment. | Không có Enrollment hợp lệ/Attempt; không tiết lộ đề. | PASS-AUTO: 4 biến thể. | [Paid access](../../tests/EnglishLearningPlatform.IntegrationTests/PaidEnrollmentAssessmentAccessTests.cs): `PaidEnrollmentAssessmentAccessTests.PaymentWithoutVerifiedSuccess_DeniesEnrollmentLearningAndAssessment`. |
| AC-07 | Enrollment free hợp lệ hoặc paid đã có Payment hợp lệ. | Đọc overview và taking sheet của lượt Student. | Được truy cập đúng Course/Assessment. | PASS-AUTO: cả free và paid. | [Query](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentQueryServiceTests.cs): `AssessmentQueryServiceTests.OverviewAndTakingSheet_AllowValidFreeAndPaidEnrollment`. |
| AC-08 | Đã có Attempt, nhưng Payment/Order bị sai trạng thái, buyer, Course, amount hoặc currency. | Resume/đọc taking sheet. | Không coi hàng Enrollment đơn thuần là đủ quyền; không sửa deadline hoặc chấm trái quyền. | PASS-AUTO: 7 biến thể. | [Paid access](../../tests/EnglishLearningPlatform.IntegrationTests/PaidEnrollmentAssessmentAccessTests.cs): `PaidEnrollmentAssessmentAccessTests.InvalidPaidEnrollment_DeniesResumeAndTakingQueries`. |
| AC-09 | Assessment chuyển Independent sang Course-linked rồi chuyển/gỡ trước Attempt đầu tiên. | Student trong/ngoài Course đọc overview và Start. | Quyền đổi đúng scope; Independent Published cho Student đăng nhập; Course-linked yêu cầu Enrollment. | PASS-AUTO; race Start/đổi scope đã có test riêng, không cần viết lại. | [Scope](../../tests/EnglishLearningPlatform.IntegrationTests/TeacherAssessmentScopeTests.cs): `TeacherAssessmentScopeTests.ScopeTransition_ImmediatelyChangesStudentOverviewAndStartEnrollmentRequirements`; `TeacherAssessmentScopeTests.ConcurrentStartAndScopeChange_RecheckLockedState_WithoutTimingDependentSleeps`. |
| AC-10 | Audio nội bộ thuộc Course; chưa enroll, Pending/Failed/Cancelled/Succeeded; có Attempt hoặc PracticeSubmission cũ. | GET URL bằng actor đối chứng; Archive Course rồi chuyển Payment Pending/xóa Enrollment. | Archive không tự thu hồi quyền lịch sử; mất Enrollment hợp lệ không được dùng lịch sử vượt quyền audio. | PASS-AUTO QA4: 6 biến thể; request bị chặn không chấm hoặc đổi deadline. Đã sửa nhánh lịch sử thiếu Enrollment. | [Stimulus](../../tests/EnglishLearningPlatform.IntegrationTests/StimulusCreationTests.cs): `StimulusCreationTests.ManagedAudio_CourseScopeHonorsPaymentAndOwnership`. |
| AC-11 | Teacher B có Id và version của đề A, CSRF đúng của B. | POST Clone trực tiếp, gửi owner giả. | 404, không thêm Assessment và không sửa snapshot đề A. Owner thật vẫn Clone được. | PASS-AUTO QA4; bổ sung vào test có sẵn, không viết lại suite Clone. | [Clone](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentCloneTests.cs): `AssessmentCloneTests.Clone_DeniesStaleVersionForeignOwnerStudentAndMissingCsrf_HttpRedirectsToNewDraft`. |
| AC-12 | Student đã mở/lưu bài khi còn quyền; sau đó mất Enrollment hoặc Payment Pending/Failed. | GET Take, POST Start/Save/Submit từ tab cũ; gọi SaveAnswer/FinalizeExpired trực tiếp. | Từ chối; không chấm sớm, đổi đáp án/deadline/timestamp hay thêm kết quả. | PASS-AUTO QA4 source cuối: 3 biến thể mới; đã sửa kiểm tra Enrollment khi Save và fallback HTTP chỉ chấm khi hết hạn. Giữ nguyên contract chấm lịch sử nội bộ/completion cũ. | [HTTP](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentUiFlowTests.cs): `AssessmentUiFlowTests.StaleTakingTab_InvalidEnrollment_BlocksStartSaveAndEarlySubmitWithoutWrites`. |
| AC-13 | Student mở Independent Published trước khi Teacher gắn Course. | Bấm Start bằng token cũ sau khi gắn Course; gỡ về Independent rồi thử lại. | Tab cũ bị chặn, không tạo lượt/đổi định nghĩa. Sau gỡ Course được xem/Start mà không cần Enrollment. | PASS-AUTO QA4; cùng với race Start/scope sẵn có chứng minh recheck server. | [Scope](../../tests/EnglishLearningPlatform.IntegrationTests/TeacherAssessmentScopeTests.cs): `TeacherAssessmentScopeTests.Http_StaleIndependentStart_RechecksCourseScope_AndDetachRestoresIndependentAccess`. |

### 3.4. Không tạo kết quả trùng — chuyển tiếp A2/A3

Trong code hiện tại, kết quả Assessment nằm trên `Attempt` và
`AttemptSkillResults`, không có bảng `Result` độc lập. Practice dùng
`PracticeSubmission`/`PracticeAnswer`. Vì vậy kiểm tra không trùng phải gồm cả
ID, số hàng, điểm, đáp án, thời điểm chấm và unique key, không chỉ đếm một bảng Result.

| Mã | Điều kiện ban đầu | Thao tác | Kết quả mong đợi | Kết quả thực tế | Test hiện có |
| --- | --- | --- | --- | --- | --- |
| ND-01 | Student chưa có Attempt active trên Assessment. | 5 request Start đồng thời. | Một Attempt active, cùng Id/deadline trả về. | PASS-AUTO: 1 Started, còn lại Resumed, DB chỉ 1 Attempt. | [Start](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentAttemptStartTests.cs): `AssessmentAttemptStartTests.ConcurrentStarts_ReuseOneActiveAttempt`. |
| ND-02 | Attempt có một câu đúng trong đề 5 câu. | 5 request Finalize đồng thời. | Chỉ một request chấm thật; các request còn lại dùng kết quả đã có. | PASS-AUTO: 1 Graded + 4 AlreadyGraded, cùng điểm 20%. | [Start](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentAttemptStartTests.cs): `AssessmentAttemptStartTests.ConcurrentFinalization_GradesOnlyOnce`. |
| ND-03 | Đã lưu 80%, deadline vừa qua. | Một tab Save đáp án mới, tab khác Submit/bỏ chọn đồng thời. | Một lần chấm hết giờ; điểm và Answer cũ không đổi; mỗi skill chỉ một hàng. | PASS-AUTO: 1 Attempt, 2 SkillResult, giữ nguyên Answer Id/lựa chọn/timestamp. | [HTTP](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentUiFlowTests.cs): `AssessmentUiFlowTests.ConcurrentLateSaveAndSubmit_IgnoreChangedAnswersAndGradeOneResult`. |
| ND-04 | Practice đang khả dụng, Student đã enroll. | Làm lại 10 lần với đáp án đúng/sai xen kẽ. | Không giới hạn retake; chỉ giữ kết quả và đáp án gần nhất. | PASS-AUTO: cùng SubmissionId, mỗi lần đúng 1 submission/answer, điểm 100%/0% tương ứng. | [Practice runtime](../../tests/EnglishLearningPlatform.IntegrationTests/PracticeQuestionValidationTests.cs): `PracticeQuestionValidationTests.RepeatedRetakes_KeepOneLatestResultAndLatestSelectedAnswer`. |
| ND-05 | Cùng Student/Practice nhận nhiều lần submit. | 6 retake đồng thời, các bộ đáp án khác nhau. | Chỉ một kết quả gần nhất nhất quán với bộ đáp án đã lưu. | PASS-AUTO: DB chỉ một submission, điểm khớp đáp án của submission đó. Không dùng case này thay cho testcase điểm trung gian GAP-01. | [Dashboard/Practice](../../tests/EnglishLearningPlatform.IntegrationTests/LearningDashboardFeatureTests.cs): `LearningDashboardFeatureTests.Practice_ConcurrentRetakesKeepOneConsistentLatestSubmission`. |

### 3.5. Teacher, Stimulus và bảo toàn lịch sử — nền cho UAT A5

| Mã | Điều kiện ban đầu | Thao tác | Kết quả mong đợi | Kết quả thực tế | Test hiện có |
| --- | --- | --- | --- | --- | --- |
| FL-01 | Teacher tạo mỗi loại PlacementTest, SkillAssessment, PracticeExam. | Lưu Draft rỗng và mở lại. | Owner đúng tài khoản, cấu hình được giữ; Draft chưa có câu vẫn lưu được. | PASS-AUTO: 3 loại. | [Draft](../../tests/EnglishLearningPlatform.IntegrationTests/TeacherAssessmentDraftTests.cs): `TeacherAssessmentDraftTests.Create_AllThreeTypes_SavesOwnedEmptyDraftAndReopensConfiguration`. |
| FL-02 | Owner có đề Draft hoặc Published, kể cả có lịch sử. | Preview và tải lại 3 lần. | Không gọi Start, không tạo/sửa Attempt, Answer, SkillResult hoặc nội dung đề. | PASS-AUTO: so sánh snapshot ID/nội dung/điểm/timestamp, không chỉ số hàng. | [Preview](../../tests/EnglishLearningPlatform.IntegrationTests/TeacherAssessmentPreviewTests.cs): `TeacherAssessmentPreviewTests.RepeatedPreview_DoesNotCreateOrModifyAttemptsAnswersResultsOrContent`. |
| FL-03 | Đề sai metadata/points/owner/MCQ/Stimulus/TargetSkill hoặc thiếu câu. | Publish. | Báo lỗi; giữ Draft, version và toàn bộ dấu khóa cũ; không khóa dở dang. | PASS-AUTO cho các fault trong test hiện có. | [Publish](../../tests/EnglishLearningPlatform.IntegrationTests/TeacherAssessmentPublishTests.cs): `TeacherAssessmentPublishTests.Publish_InvalidDefinition_DoesNotPartiallyLockOrPublish`. |
| FL-04 | Practice Draft thiếu/sai parent, câu hỏi hoặc audio. | Publish Practice. | Chỉ nhận đúng một Lesson XOR Module cùng owner, định nghĩa hợp lệ. | PASS-AUTO: dữ liệu sai không Publish và không khóa một phần. | [Practice authoring](../../tests/EnglishLearningPlatform.IntegrationTests/TeacherPracticeQuestionsTests.cs): `TeacherPracticeQuestionsTests.Publish_InvalidDefinition_DoesNotPublishOrLockAnyContent`. |
| FL-05 | Passage hoặc audio được tạo qua form và dùng chung cho nhiều câu. | Xem Question Detail, Teacher Preview và Student sheet; GET/range audio nội bộ. | Nội dung tham chiếu nhất quán, media được trả đúng quyền. | PASS-AUTO qua HTTP; khả năng nghe thực tế bằng browser vẫn CHỜ-UI. | [Stimulus](../../tests/EnglishLearningPlatform.IntegrationTests/StimulusCreationTests.cs): `StimulusCreationTests.SharedStimulus_CreatedThroughForms_IsConsistentInDetailsPreviewAndStudentSheets`. |
| FL-06 | Question/Stimulus đã khóa; bản clone xuất phát từ Final đã có Attempt. | Gửi POST sửa trường khóa hoặc Clone rồi thử thay Final cũ. | Không sửa lịch sử; clone là Draft mới, không chép Attempt/Result và không mở khóa Question/Final. | PASS-AUTO cho POST giả mạo và luồng clone Final đã dùng. | [Lock](../../tests/EnglishLearningPlatform.IntegrationTests/ContentHistoryProtectionTests.cs): `ContentHistoryProtectionTests.ForgedQuestionPosts_CannotChangeLockedFieldsOrExplanation`; [Clone](../../tests/EnglishLearningPlatform.IntegrationTests/AssessmentCloneTests.cs): `AssessmentCloneTests.UsedFinalClone_DoesNotCopyHistoryOrUnlockContent_AndCannotReplaceOriginalFinal`. |

Các unique key đã có: `(StudentUserId, PracticeId)` tại
[PracticeSubmissionConfiguration](../../src/EnglishLearningPlatform.Infrastructure/Persistence/Configurations/PracticeSubmissionConfiguration.cs),
`(AttemptId, Skill)` tại
[AttemptSkillResultConfiguration](../../src/EnglishLearningPlatform.Infrastructure/Persistence/Configurations/AttemptSkillResultConfiguration.cs).
Unique key là lớp bảo vệ dữ liệu, không thay thế các test race ở trên.

## 4. Checklist UI thủ công mở rộng / rehearsal của nhóm

QA5 theo kịch bản người dùng đã hoàn thành bằng browser thật và xem lại ảnh,
có bằng chứng riêng. Bảng dưới rộng hơn kịch bản QA5: còn paid checkout, offline
retry, chờ timeout, đủ ba loại đề, Course Final và Clone. Giữ các ô thủ công
chưa có actual là CHỜ-UI, không đánh đồng bằng chứng tự động với chữ ký nhóm.

Tiền điều kiện chung: môi trường demo riêng; hai Teacher và hai Student có role
đúng; Course free/paid và dữ liệu mới đủ lượt. Dùng phiên browser riêng cho từng
actor. Không sửa/xóa dữ liệu người khác, không giả thanh toán thành công bằng SQL.
Không mặc định mọi skill đều có điểm hiển thị: chỉ nhóm đủ ít nhất 3 câu mới được hiện.

Mỗi dòng hiện là **CHỜ-UI của candidate đang đối chiếu**. Các lần bạn báo UI ổn
trước đây vẫn là thông tin lịch sử, nhưng không tự chuyển thành biên bản của bản
mới. Khi chạy thật, thay ô Thực tế bằng kết quả quan sát, pass/fail, giờ, SHA,
browser/viewport và đường dẫn ảnh. Lỗi phải ghi thao tác tái hiện.

| Mã | Điều kiện ban đầu | Thao tác | Kết quả mong đợi | Kết quả thực tế |
| --- | --- | --- | --- | --- |
| UI-01 | Teacher A có file audio hợp lệ và một đoạn passage mới. | Tạo Stimulus; tạo Reading/Listening Question; tìm/lọc rồi mở Detail. | Chỉ thấy dữ liệu của A; passage đúng; audio nghe được; form lỗi giữ dữ liệu và có thông báo. | CHỜ-UI — chưa ghi actual/evidence candidate hiện tại. |
| UI-02 | Course của A có Lesson Published trong Module Visible. | Tạo Practice, chọn Lesson hoặc Module, thêm/bỏ/sắp xếp câu, lưu và Publish. | Reload đúng câu/thứ tự; đúng một parent; lỗi Publish hiển thị rõ. | CHỜ-UI — chưa ghi actual/evidence candidate hiện tại. |
| UI-03 | Student đã enroll; Practice UI-02 còn khả dụng. | Làm và nộp; xem đáp án/giải thích; làm lại với lựa chọn khác. | Tự chấm equal-weight; có feedback; kết quả mới thay kết quả cũ, không có history nhiều lượt Practice. | CHỜ-UI — chưa ghi actual/evidence candidate hiện tại. |
| UI-04 | Teacher có bộ câu hợp lệ; đủ 5 câu/đề và đúng TargetSkill khi cần. | Tạo cả 3 loại; cấu hình; lưu câu/points; Preview rồi Publish. | Cấu hình gọn, nút rõ; Placement không dùng PassingScore/nhãn trình độ; Preview không Start. Việc DB không đổi đã có FL-02, không cần đoán qua màn hình. | CHỜ-UI — chưa ghi actual/evidence candidate hiện tại. |
| UI-05 | Có Independent Published và Course-linked Published; Student chưa enroll, pending payment và đã thanh toán hợp lệ. | Mở danh sách, URL trực tiếp và Start bằng từng tài khoản. | Independent khả dụng; Course-linked chưa có quyền bị chặn; sau Enrollment hợp lệ mới xem/Start được. | CHỜ-UI — chưa ghi actual/evidence candidate hiện tại. |
| UI-06 | Student Start đề mới, còn hạn. | Lưu đáp án; F5; đóng tab; đăng nhập lại và Resume. | Đúng lượt và đáp án đã lưu; thời gian tiếp tục giảm, không bắt đầu lại Duration. | CHỜ-UI — chưa ghi actual/evidence candidate hiện tại. |
| UI-07 | Attempt còn hạn, có nhiều câu và autosave đang hoạt động. | Đổi/bỏ chọn; Câu trước/Câu tiếp/chọn số câu; thử mất mạng ngắn rồi retry lưu. | Không mất lựa chọn trên tab; chỉ báo đã lưu khi server xác nhận; mất mạng không được giả báo lưu thành công. | CHỜ-UI — chưa ghi actual/evidence candidate hiện tại. |
| UI-08 | Đề demo Duration=1 phút được tạo mới trước Start. | Lưu vài câu; chờ deadline; thử đổi/nộp sau giờ; đóng tab rồi mở lại Take/History/Result sau deadline. | Khóa trả lời, chấm đáp án hợp lệ cũ, hiện lý do hết giờ; không tự tạo lượt mới. Chỉ đọc History sau hạn cũng chấm lượt cũ, không phụ thuộc POST từ browser. | CHỜ-UI — chưa ghi actual/evidence candidate hiện tại. |
| UI-09 | Đề points đã biết, MaxAttempts đủ 3; có kế hoạch đáp án cho 60%, 85%, 70%. | Làm ba lượt, mở từng Result, History, Dashboard và Profile. | History đủ ba lượt; đại diện 85% cùng Skill của lượt đó; không có đáp án chi tiết Assessment. | CHỜ-UI — chưa ghi actual/evidence candidate hiện tại. |
| UI-10 | Có Teacher B và Student B, khác owner/người làm. | Dùng phiên riêng mở URL editor/Preview/Result của A; thử form hợp lệ trong dữ liệu demo riêng. | Từ chối có kiểm soát; không hiển thị đề/đáp án hoặc sửa dữ liệu của A. | CHỜ-UI — chưa ghi actual/evidence candidate hiện tại. |
| UI-11 | Teacher có hai Course của mình, đề chưa có Attempt và Final đủ điều kiện. | Gắn/chuyển/gỡ Course; chọn/thay/gỡ Final chưa dùng; sau Student Start thử lại từ tab cũ. | Scope đổi đúng trước Attempt; không tự gỡ Final; sau Attempt các thay đổi bị khóa; completion/history còn nguyên. | CHỜ-UI — chưa ghi actual/evidence candidate hiện tại. |
| UI-12 | Có Assessment/Final đã dùng, Question đã khóa. | Clone; sửa cấu hình/câu/points ở Draft mới; thử thay Final đã dùng. | Clone độc lập, bản gốc không đổi; không sao chép lịch sử; Question cũ vẫn khóa; không vượt khóa Final. | CHỜ-UI — chưa ghi actual/evidence candidate hiện tại. |
| UI-13 | Có dữ liệu thật trên Question Bank, TeacherPractices, Assessment và Result. | Xem ở 1440px và 390px; mở menu, bấm nút, cuộn bảng và nghe audio. | Nội dung không mất ở full màn hình; nút thao tác rõ, dùng được; không tràn che form/nội dung. | CHỜ-UI — chưa ghi actual/evidence candidate hiện tại. |

## 5. Browser automation có sẵn — không nhầm với nghiệm thu UI thủ công

| Nguồn | Hiện trạng đã đọc | Dùng thế nào ở A5 |
| --- | --- | --- |
| [qa5-journey-uat.mjs](../../tests/browser/qa5-journey-uat.mjs) | Đã chạy trên app/SQL riêng: 6/6 kịch bản từ tạo passage/audio, Question, Practice Preview/Publish, retake, Assessment Preview/Publish/Resume/Submit đến History/Dashboard/Profile. Lượt `browser-run-06`: 48 ảnh 1440/390px, 0 pageerror, có base commit + dirty flag + source digest kiểm lại cuối run. | Chạy bằng [runner QA5](../../scripts/qa5-browser-uat.ps1), không sửa SQL nghiệp vụ. Xem báo cáo QA5 để biết đúng phạm vi đã pass và phần rehearsal mở rộng chưa chạy. |
| [assessment-ui.test.mjs](../../tests/browser/assessment-ui.test.mjs) | Có 9 test browser trên HTML fixture, dùng `assessment.js` thật: confirm/cancel, navigation, autosave/retry/order, pending save và deadline. A3 chạy lại 9/9 pass; `TestResults/qa3/qa3-browser.xml`. Không nằm trong TRX .NET. | Không coi fixture là UAT toàn app/backend/role/SQL. Test trang Expired cũ kiểm tra fallback, không thay bằng chứng lazy-finalize HTTP ở DL-11/12. Không viết lại 9 test đã có. |
| [assessment-types-uat.mjs](../../tests/browser/assessment-types-uat.mjs) | Có hai report local lịch sử: `TestResults/t2-browser-uat/assessment-types-results.json` và bản trong `t3-browser-uat/`; mỗi report 21/21 Passed, 0 JavaScript error. Report không ghi SHA/source digest. | Tái sử dụng suite `npm run test:assessment-types` trên app/DB riêng. Không cộng hai lần chạy thành 42 testcase khác nhau và không gán kết quả cũ cho commit hiện tại. |
| [assessment-scope-uat.mjs](../../tests/browser/assessment-scope-uat.mjs) | Report lịch sử `TestResults/t3-browser-uat/assessment-scope-results.json`: 8/8 Passed, 0 JavaScript error; không có SHA/source digest. | Tái sử dụng `npm run test:assessment-scope`; report này không kiểm tra thanh toán, phần đó có integration ở AC-06/AC-08. |
| [README browser](../../tests/browser/README.md) | Đã có hướng dẫn môi trường riêng và biến cấu hình; suite UAT chính có quy ước ghi candidate. | Áp dụng quy ước evidence cho các lượt browser mới; tuyệt đối không dùng DB đang làm việc để chạy script sinh dữ liệu. |

## 6. Phần còn thiếu, lệch rule hoặc cần bổ sung bằng chứng

| Mã | Loại / bước xử lý | Điều kiện và thao tác cần kiểm | Mong đợi theo yêu cầu | Thực tế hiện tại và việc làm tiếp |
| --- | --- | --- | --- | --- |
| GAP-01 | ĐÃ ĐÓNG — QA2 | Practice có 3 câu; trả lời 1 đúng, 1 sai, 1 bỏ trống rồi Submit bằng service thật. | Điểm 1/3 × 100, phản hồi từng câu đúng/sai/trống; giữ chính sách latest-only. | Đã thêm đúng một test SC-12, pass trên local và Docker; 33,33% → 100%, giữ cùng SubmissionId và đúng Answer mới nhất. Xem [báo cáo QA2](qa2-scoring-results.md). Số 50% seed trong test lifecycle vẫn không được dùng làm bằng chứng chấm. |
| GAP-02 | ĐÃ XỬ LÝ đường Student — A3; đường Teacher chưa thuộc phạm vi này | Attempt quá deadline nhưng chưa có Save/Start/Submit mới; đọc Take/History/Result của mình hoặc đường Teacher xem Attempt. | BR08 và AC-SUB-05/09 yêu cầu lazy-finalize khi đọc; không cần Student quay lại gửi Submit. | Đã thêm write service chỉ chấm khi hết hạn và gọi tại HTTP Student Take/History/Result, giữ query thuần read-only. DL-09–12 pass, trước hạn không chấm, F5 không chấm lại. Chưa triển khai màn hình/API Teacher đọc Attempt trong A3; không coi toàn bộ AC-SUB-09 cho Teacher đã hoàn thành. Khi bổ sung báo cáo Teacher phải tái sử dụng rule này và kiểm tra owner. Xem [A3](qa3-timeout-resume-results.md). |
| GAP-03 | ĐÃ CÓ bằng chứng QA5; rehearsal mở rộng/bàn giao ở QA6 | Chạy browser thật trên app/source hiện tại; phân biệt phạm vi QA5 và UI-01–13 mở rộng. | Có actual, SHA/source, thời gian, actor, browser, viewport và ảnh; lỗi có retest. | QA5 có source digest runtime/browser + base commit/dirty flag, 6 kịch bản live pass, 48 ảnh và retest CSS mobile. Preview Practice riêng đã được bổ sung theo yêu cầu, nên dùng suite hành trình QA5 mới thay vì viết trùng các unit/service test. Không tự tick đủ ba loại đề, paid checkout, offline/timeout và Course Final/Clone trên browser hôm nay. QA6 cần đóng gói artifact và ký nhận rehearsal nhóm nếu yêu cầu. |
| GAP-04 | ĐÃ CHUẨN BỊ — QA6 local; chờ nhóm nhận/rehearsal/CI | Chuẩn bị testcase/báo cáo/demo của candidate cuối cùng. | Report, dữ liệu demo, hướng dẫn setup, runbook và artifact thống nhất cùng version. | [QA6](qa6-handover-results.md) có CSV actual từ TRX, ZIP ảnh/log/error-retest và manifest SHA256; launcher fresh đã migrate/seed/login đủ5actor. Có runbook và demo không sửa SQL. Base SHA/dirty source và evidence từng lượt được tách rõ; chưa tự nhận CI hoặc chữ ký nhóm. |

Không có test fail trong hai TRX baseline đã đọc. Điều đó **không** có nghĩa mọi
AC đã hoàn thành. QA2 đã đóng GAP-01 bằng test trực tiếp; A3 xử lý GAP-02 trên
đường Student, còn Teacher report/UI/báo cáo cần phạm vi và bằng chứng riêng. Lượt QA2 Docker 113/113 pass;
hai lỗi SQL local ban đầu và retry được giữ rõ trong báo cáo QA2, không xóa lịch sử.

Giới hạn audio đã được ghi tại
[stimulus-audio-history.md](../architecture/stimulus-audio-history.md): file upload
được quản lý theo hash; audio URL ngoài lịch sử không thể bảo đảm bên cung cấp
không thay bytes. Khi UAT phải phân biệt hai loại, không gọi URL ngoài là bất biến.

## 7. Thứ tự tiếp theo và nguyên tắc không viết trùng

1. **QA2/A2 — đã xong:** tái sử dụng SC/ND, bù một test SC-12 và đóng GAP-01;
   8 unit + 113 integration case đã pass trên môi trường chuẩn. Xem báo cáo QA2.
2. **A3 — đã xong phạm vi Student:** tái sử dụng DL/ND, bổ sung kiểm tra từng tick,
   race Submit/expiry và lazy-finalize HTTP. Xem DL-09–12 và báo cáo A3; không
   viết lại concurrency Start/Submit đã có. Đường báo cáo Attempt cho Teacher
   chưa có trong task này, không tự nghiệm thu phần đó của AC-SUB-09.
3. **QA4/A4 — đã xong:** chạy lại AC-01–13; thêm 2 method test/4 biến thể và mở
   rộng audio, Clone/Student khác; sửa tab mất quyền và đường audio lịch sử.
   390 unit + 435 integration case pass trên source cuối, xem báo cáo QA4.
4. **QA5/A5 — đã xong kịch bản được giao:** thêm Preview Practice theo yêu cầu;
   6 luồng live browser pass, 48 ảnh, 0 pageerror; sửa và retest bảng mobile.
   Xem báo cáo QA5; không gộp thành nghiệm thu mọi dòng UI mở rộng hoặc mọi browser.
5. **QA6 — đã chuẩn bị local:** chốt artifact, testcase, báo cáo, launcher và demo;
   xem báo cáo QA6. Cần nhóm nhận artifact/rehearsal và review/CI khi phát hành.

Các filter có thể cùng chạy một test: không cộng số lượng từ nhiều lệnh filter
thành tổng duy nhất. Khi đóng một gap, dẫn tới test nguồn hoặc evidence thật và
cập nhật ô Thực tế; không xóa testcase cũ chỉ vì task trước đã pass.

### Checklist hoàn thành A1

- [x] Đối chiếu yêu cầu với test và phần code liên quan.
- [x] Có mã, tiền điều kiện, thao tác, expected và actual cho từng testcase logic.
- [x] Tách unit/integration, browser automation và UI thủ công.
- [x] Đọc kết quả TRX thực tế; ghi đúng phạm vi và giới hạn gắn candidate.
- [x] Chỉ rõ phần pass, thiếu test, lệch rule và thiếu bằng chứng.
- [x] Có nơi tái sử dụng test và thứ tự A2–A6, chưa viết thêm test trùng.

**A1 hoàn thành về lập danh sách và hiện trạng. QA2/A2, A3 phạm vi Student, QA4
và QA5 theo kịch bản được giao đã có bằng chứng riêng. QA6 đã chuẩn bị bộ bàn giao
local; rehearsal/nhận bàn giao của nhóm vẫn chờ xác nhận, không tự tick các dòng
UI thủ công mở rộng chưa có actual.**
