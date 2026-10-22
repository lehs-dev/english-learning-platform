# QA2 — Scoring và không tạo kết quả trùng

Ngày kiểm tra: **21/10/2026, Asia/Bangkok**.

**Kết luận: QA2 đạt trên môi trường chuẩn SQL Server 2022 Docker.**

## 1. Phạm vi và phiên bản

- Mốc code: `3f45ac1501c05db4b7a6b70975c37c1add05e8c2`, cộng thay đổi test QA2 chưa commit.
- Không sửa production code, schema, migration hay cấu hình chạy ứng dụng.
- Chỉ thêm **1 test**, tái sử dụng các test scoring, history và concurrency đang có.
- .NET SDK: `10.0.100`; build/test cấu hình Release.
- Môi trường integration dùng để chốt: SQL Server 2022 Testcontainers
  (`mcr.microsoft.com/mssql/server:2022-latest`), Docker Engine `29.4.0`.
- Khi Docker chưa sẵn sàng, đã thử SQL Server local `17.0.1000.7` bằng Windows
  Authentication. Lượt thử và retry được giữ riêng ở phần 3, không trộn vào kết quả Docker.
- Mỗi fixture tạo database `EnglishLearningPlatformTests_<ngẫu nhiên>` và kho audio
  test riêng; không dùng database hoặc audio của đồ án. Demo seeder được tắt khi test.

Thay đổi tại
[LearningDashboardFeatureTests.cs](../../tests/EnglishLearningPlatform.IntegrationTests/LearningDashboardFeatureTests.cs):
`LearningDashboardFeatureTests.Practice_MixedCorrectWrongAndBlankAnswers_UseEqualWeightsAndRetakeReplacesLatestResult`.
Helper seed nhận số câu Practice, mặc định vẫn là 2 để giữ nguyên các test cũ;
test mới yêu cầu 3 câu ngay lúc tạo fixture, không sửa đề sau Publish.

SHA-256 của file test khi build/chạy QA2:
`496D343508370EA21DC3281FA34473FB20A113FF5EE2C63D63B0E50BE92B157B`.

## 2. Bảng tính tay và test đối chứng

| Mã | Tình huống | Tính tay / kết quả mong đợi | Test đối chứng | Kết quả thực tế QA2 |
| --- | --- | --- | --- | --- |
| QA2-01 | Hai đề dùng chung Question nhưng points lần lượt 1/3 và 3/1; đúng câu đầu. | 1/4 = 25%; 3/4 = 75%; không có điểm toàn cục ở Question Bank. | `ExamScoringServiceTests.Score_SharedQuestions_UsePointsOfEachAssessmentWithoutChangingQuestionBank`. | PASS: 25% và 75%, Question/Option không đổi. |
| QA2-02 | Points 1,25; 2,5; 0,75; 3; đúng câu đầu, sai câu hai, câu ba bỏ chọn, câu bốn không gửi. | 1,25/7,5 × 100 = 16,67%; câu sai/trống nhận 0, vẫn nằm trong mẫu số. | `ExamScoringServiceTests.Score_FractionalPointsAndBlankAnswers_CountEveryQuestionInMaximumScore`. | PASS: 1,25 điểm đạt, 7,5 tối đa, 16,67%. |
| QA2-03 | Hai Skill, mỗi Skill tối đa 5; đạt Grammar 1,25 và Vocabulary 3,75. | Overall 5/10 = 50%; Grammar 1,25/5 = 25%; Vocabulary 3,75/5 = 75%. | `AssessmentQueryServiceTests.FinalizedAttempt_FractionalPointsAndBlanks_PersistSeparateSkillDenominators`. | PASS: điểm DB và kết quả đọc lại khớp; Vocabulary chỉ có 2 câu nên điểm vẫn lưu nhưng không hiện breakdown. |
| QA2-04 | Raw Overall 69,996%; PassingScore 70%. | Hiển thị 70% nhưng Fail; không ghi CompletedAt. Raw Skill 59,996% vẫn yếu, đúng 60% không yếu. | `CourseCompletionIntegrationTests.RoundedDisplayScoreAtPassingBoundary_DoesNotCompleteFailedFinal`; `AssessmentQueryServiceTests.Result_WeakSkillUsesRawPoints_NotRoundedScoreOrPassingScore`. | PASS: Fail ở biên làm tròn; các biến thể raw Skill/0%/60% và unit SkillAnalysis đều đúng. |
| QA2-05 | Đề 3 Reading + 2 Listening; biến thể không Skill nào đủ 3 câu. | Chỉ Reading đủ 3 câu được hiện; không biến nhóm thiếu dữ liệu thành 0%; không xóa SkillResult trong DB hoặc lộ đáp án Assessment. | `AssessmentUiFlowTests.Result_OnlyShowsSkillsWithThreeQuestions_WithoutExposingOrDeletingAnswers`. | PASS: cả 2 biến thể, DTO/HTML đúng và SkillResult/Answer còn nguyên. |
| QA2-06 | Năm request Finalize cùng Attempt; một câu đúng trong đề 5 câu equal points. | 1 Graded + 4 AlreadyGraded; tất cả trả 20%; không chấm nhiều lần. | `AssessmentAttemptStartTests.ConcurrentFinalization_GradesOnlyOnce`; `AssessmentAttemptStartTests.ConcurrentSaveAndFinalize_PreservesGradedResult`. | PASS: chấm đúng một lần; race Save/Finalize giữ điểm khớp bộ đáp án được chấm. |
| QA2-07 | Submit thành công 80% rồi gửi lại payload 0%; hoặc Save/Submit muộn đồng thời. | Giữ điểm, Answer và thời điểm chấm cũ; mỗi Skill chỉ một kết quả, không thêm Attempt. | `AssessmentUiFlowTests.SubmitPassedFinal_ShowsSkillScoresAndCompletion_PreservesHistoryAfterLessonAddedAndReplay`; `AssessmentUiFlowTests.ConcurrentLateSaveAndSubmit_IgnoreChangedAnswersAndGradeOneResult`. | PASS: giữ 80%, timestamp/đáp án cũ; case race có 1 Attempt và 2 SkillResult. |
| QA2-08 | Practice có 3 câu: 1 đúng, 1 sai, 1 bỏ trống; làm lại đúng cả 3. | 1/3 = 33,33%, sau đó 100%; cùng SubmissionId; lần đầu có 2 Answer, lần sau có đúng 3 Answer mới nhất. | `LearningDashboardFeatureTests.Practice_MixedCorrectWrongAndBlankAnswers_UseEqualWeightsAndRetakeReplacesLatestResult` — test mới đóng GAP-01. | PASS trên local và Docker: 33,33% → 100%, đúng feedback/Answer và cùng SubmissionId. |
| QA2-09 | Practice retake 10 lần hoặc 6 retake đồng thời. | Không giới hạn retake; chỉ một submission gần nhất nhất quán với đáp án đã lưu. | `PracticeQuestionValidationTests.RepeatedRetakes_KeepOneLatestResultAndLatestSelectedAnswer`; `LearningDashboardFeatureTests.Practice_ConcurrentRetakesKeepOneConsistentLatestSubmission`. | PASS: 10 retake giữ cùng ID; 6 request đồng thời vẫn một submission và điểm khớp đáp án. |
| QA2-10 | Assessment có ba lượt 60%, 85%, 70%. | History giữ đủ ba lượt; Dashboard/Profile chọn 85% và Skill cùng lượt; lượt mới không sửa history cũ. | `AssessmentUiFlowTests.History_ThreeWeightedAttemptsRemainSeparateAfterWithdrawalAndEnrollmentRevocation`; `LearningDashboardFeatureTests.DashboardAndProfile_KeepHighest85AndItsSkills_WhenLatestAttemptScores70`. | PASS: history và snapshot từng lượt không đổi; Dashboard/Profile cùng chọn lượt 85%. |
| QA2-11 | Nhiều lượt đồng highest; hoặc chưa có lượt đã chấm. | Tie-break FinalizedAt mới nhất rồi Id lớn hơn; chưa có dữ liệu khác 0%. | `LearningDashboardFeatureTests.Dashboard_UsesRepresentativeAttemptWithSameSkillDataAndDeterministicTies`; `LearningDashboardFeatureTests.Dashboard_WithoutEvidenceDoesNotInventScoresOrRecommendations_AndScopesToStudent`. | PASS: chọn đúng lượt/Skill; không tự tạo điểm khi thiếu kết quả. |

Kết quả Assessment nằm trên `Attempt` và `AttemptSkillResults`, không có bảng
`Result` độc lập. Practice dùng `PracticeSubmission`/`PracticeAnswer`. Các test
đối chiếu cả số hàng, ID, điểm, đáp án và timestamp để tránh bỏ sót việc ghi đè.

## 3. Lượt chạy QA2

| Kiểm tra | Kết quả |
| --- | --- |
| Build solution Release | PASS: 0 warning, 0 error. |
| Unit scoring/SkillAnalysis | PASS: 8/8, 0 fail, 0 bỏ qua; 17:21:59–17:22:00 (+07:00); report `TestResults/qa2/qa2-unit.trx`. |
| Thử integration SQL local | 111/113 pass, 2 fail; `TestResults/qa2/qa2-integration.trx`. Hai case HTTP gặp SQL exception sau khoảng 30 giây. |
| Retry riêng các case lỗi trên SQL local | PASS: 3/3 (hai method, một method có hai biến thể); `TestResults/qa2/qa2-local-retry.trx`. Không sửa source/expected giữa hai lượt; lỗi chưa tái hiện. |
| Integration Docker được chọn bên dưới | PASS: 113/113, 0 fail, 0 bỏ qua; 17:29:25–17:30:52 (+07:00); report `TestResults/qa2/qa2-integration-docker.trx`. |
| Database test tự dọn | Đã xác nhận local còn 0 database có prefix test; không còn container mang label Testcontainers sau lượt Docker. |
| Rà secret trong working tree | 0 finding; `TestResults/qa2/security-audit.txt`. Lượt này không quét lại lịch sử Git. |

Không cộng kết quả QA2 với 390/1.120 của baseline A1 thành tổng testcase mới.
Các report trong `TestResults/` là artifact local được Git ignore, chưa phải CI
GitHub hoặc biên bản UI thủ công.

Hai lỗi local được giữ trong report gốc, không xóa hoặc đổi thành Passed:
`AssessmentUiFlowTests.ExpiredPost_GradesSavedAnswersOnceAndIgnoresLateChanges`
(biến thể Submit) và
`AssessmentUiFlowTests.SaveResume_RejectsMalformedForeignAndDuplicateAnswersBeforeWriting_AndEncodesContent`.
Chưa đủ log để kết luận nguyên nhân SQL exception; không gọi retry pass là đã
sửa lỗi production. QA2 được kiểm chứng lại toàn bộ trên backend Docker chuẩn.
Ba case retry không được cộng thêm vào số testcase độc lập của nhóm QA2.

Phân bố 113 case trong lượt Docker: AssessmentQueryServiceTests 44,
AssessmentUiFlowTests 17, LearningDashboardFeatureTests 17,
CourseCompletionIntegrationTests 24, AssessmentAttemptStartTests 2,
PracticeQuestionValidationTests 9.

Nhóm integration đã chọn, chạy **một lần trong một lệnh filter OR**, không cộng
đúp các test cùng khớp filter:

- Toàn bộ `AssessmentQueryServiceTests`, `AssessmentUiFlowTests`,
  `LearningDashboardFeatureTests`, `CourseCompletionIntegrationTests`.
- Hai test `AssessmentAttemptStartTests.ConcurrentFinalization_GradesOnlyOnce`
  và `AssessmentAttemptStartTests.ConcurrentSaveAndFinalize_PreservesGradedResult`.
- `PracticeQuestionValidationTests.RepeatedRetakes_KeepOneLatestResultAndLatestSelectedAnswer`
  và các biến thể `PracticeQuestionValidationTests.ValidExistingPractice_AllowsSheetSubmitAndFeedback`.
- Dùng `tests/low-memory.runsettings` để chạy tuần tự.

## 4. Kết luận

**QA2 hoàn thành:** tính tay khớp hệ thống; Submit lặp/đồng thời không sinh kết
quả trùng; Practice giữ latest-only, Assessment giữ nhiều Attempt và chọn highest.
Đã đóng GAP-01 trong [checklist A1](practice-assessment-a1-checklist.md).

Chỉ bổ sung test và tài liệu. Chưa commit/push, chưa tuyên bố CI GitHub đã pass.
Không sửa GAP-02/lazy-finalize trong QA2: phần này thuộc QA3. UAT browser thật
và biên bản demo vẫn thuộc QA5/QA6. Không tự chuyển các mục đó sang Passed.
