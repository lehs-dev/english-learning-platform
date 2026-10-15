# Kiểm thử Learning/Commerce và UAT Tuần 6

Ngày thực hiện: 14/10/2026, Asia/Saigon. Nhiệm vụ theo bảng người dùng cung cấp: kiểm thử Learning + Commerce (13,0%) và UAT/báo cáo (13,2%). Branch `feat/learning-commerce-uat`, base `aaaba93`.

## Phạm vi thay đổi

- Thêm `LearningCommerceJourneyTests.cs`: 15 integration case mới, dùng CheckoutTestFactory hiện có và database riêng.
- Thêm `tests/browser/learning-commerce-uat.mjs`: 10 hành trình Learning/Commerce qua Chromium, có expected/actual và ảnh.
- Thêm gate PowerShell và SQL evidence chỉ đọc; tạo SQL Server 2022/demo riêng, kiểm tra và cleanup sau run.
- Hoàn thiện [chương Learning/Commerce](../handover/learning-commerce-chapter.md), testcase và hồ sơ evidence.

Source production `src/`, EF schema/migration, giao diện và workflow CI hiện có được giữ nguyên. Những phát hiện ngoài phạm vi được ghi để owner xử lý.

## Ma trận testcase

| ID | Điều kiện/thao tác | Expected và dữ liệu kiểm chứng | Test |
| --- | --- | --- | --- |
| LC-01 | Teacher khác, token CSRF hợp lệ, POST Edit/Content/Reorder/DeleteResource/Publish/Unpublish/Archive | Từ chối 7 action; Course/owner/giá/lesson/resource/payment/enrollment/progress giữ nguyên | `ForeignTeacherMutationWithValidCsrf_CannotAlterCourseOrCommerceOrStudentProgress` — 7 case |
| LC-02 | Free enroll 8 POST đồng thời, spoof StudentUserId | 1 Enrollment của principal, PaymentId null, không Order/Payment cho Course | `ConcurrentFreeEnrollmentHttpPosts_IgnoreSpoofedIdentityAndCreateOneEnrollmentWithoutCommerceRows` |
| LC-03 | Paid pending, return URL giả, payment fail, callback success xung đột, mua lại | Pending/Failed không mở Lesson; Failed terminal; đơn retry mới, pending retry được reuse; success tạo quyền đúng buyer/quote | `PaidFailureThenRetrySuccess_OpensLearningOnceAndPreservesProgressOnCallbackReplay` |
| LC-04 | Sau success và học đủ bài, callback lặp 6 request đồng thời | Một receipt success/Enrollment; progress và timestamp giữ nguyên; report chỉ cộng 199000 VND | Cùng test LC-03; `CheckoutFeatureTests` bổ sung hosted HMAC/concurrency |
| LC-05 | Unpublish/Archive sau tạo Order, callback Success đến muộn rồi replay | Lưu 1 receipt success; không Enrollment, không progress, Lesson bị chặn khi khóa còn withdrawn | `SuccessCallbackAfterCourseWithdrawal_RecordsReceiptAndReplayButDoesNotGrantLearning` — 2 case |
| LC-06 | Đã enroll Free/Paid, owner Unpublish rồi Archive | Unpublish chặn public/enroll mới nhưng cho học/complete; Archive cho đọc, chặn forged Completion; timestamp không đổi | `EnrolledCourse_UnpublishThenArchiveKeepsReadingButBlocksNewEnrollmentAndArchivedProgress` — 2 case |
| LC-07 | Ẩn/restore bài đã Completed, đoán Lesson/resource URL, mở Continue | Bài ẩn không đọc/ghi được; owner preview; progress loại/khôi phục trạng thái cũ; Continue chọn bài hiệu lực | `HiddenLessonAndModule_ChangeEffectiveProgressAndContinueWithoutErasingHistoricalCompletion` — 2 case Free/Paid |
| LC-08 | Publish thêm Draft, đổi Incomplete, hide/unhide toàn Module | Progress 2/2 → 2/3 → 1/3 → 0/0 (null %) → 1/3; CompletedAt giữ nguyên, report/MyCourses thống nhất | Cùng test LC-07; `EnrollmentProgressTests` |
| LC-09 | Tập Lesson hiệu lực rỗng, chưa từng hoàn thành | Không tự Complete, không ContinueLessonId | `EmptyEffectiveSet_DoesNotCompleteEnrollment` hiện có |
| LC-10 | Chữ ký/body/quote/provider sai, rollback/ACK/replay và Paid Final | Không cấp quyền sai; atomicity và idempotency | Các suite CheckoutFeature/Merge/TransactionFailure, PaidEnrollmentAssessmentAccess, CourseCompletion hiện có |

## Browser UAT và kịch bản demo

Suite seeded hiện có chạy trước để bảo toàn điều kiện ban đầu. Suite Tuần 6 tạo thêm hai Course qua form của Teacher 1; đây là dữ liệu QA trong database demo riêng, không backfill database ứng dụng.

| Case | Actor / bước | Expected |
| --- | --- | --- |
| LC-UAT-01 | Teacher 1 tạo Free/Paid, Module, 3 Lesson Text; Publish 2 Lesson và Course | Free/Paid Published, giá Paid 199000 VND, Lesson 3 Draft |
| LC-UAT-02 | Guest mở catalog/detail và đoán Lesson URL | Chỉ metadata/structure, không private text/Draft; Lesson yêu cầu Login |
| LC-UAT-03 | Teacher 2 mở Manage/Students và POST Archive với token | 403, khóa của Teacher 1 giữ Published |
| LC-UAT-04 | Student 1 enroll Free, thiếu CSRF rồi POST lặp hợp lệ | Thiếu token 400; một Enrollment, mở Course 0/2 |
| LC-UAT-05 | Student 2 Pending → Failed → đơn mới Success → replay | Pending/Failed chặn học; Success mở học, lịch sử đúng 2 đơn |
| LC-UAT-06 | Hai Student hoàn thành Lesson 2 trước Lesson 1 | 0/2 → 1/2 → 2/2 và ghi CompletedAt lần đầu |
| LC-UAT-07 | Teacher 1 hide/restore/publish thêm Lesson và hide/unhide Module Paid | Progress động đúng, Continue không stale, timestamp lịch sử giữ nguyên |
| LC-UAT-08 | Unpublish Free/Paid, đối chứng bằng Student chưa enroll | Public 404/new enroll bị chặn, enrolled Student đọc và đổi Incomplete được |
| LC-UAT-09 | Archive Free/Paid, POST Completion giả có CSRF, mobile 390px | Đọc được, 403 khi ghi progress, lịch sử giữ nguyên, không horizontal overflow |
| LC-UAT-10 | Owner report Paid và search không khớp Student | 1 Enrollment/1 historical completion/2 Order/199000 VND; filter không đổi aggregate |

Demo thuyết trình: mở metadata → chứng minh cần ghi danh → mô phỏng thất bại → tạo đơn mới thành công → học Lesson 2 trước → xem progress/completion → Teacher ẩn/thêm bài → so sánh progress với lịch sử → Unpublish/Archive → xem owner report. Dùng Text để luồng không phụ thuộc media ngoài. Hosted signature verification có integration evidence; sandbox không được trình bày thành thanh toán provider thật.

## Kết quả và provenance

| Kiểm chứng | Kết quả thực tế | Bằng chứng |
| --- | --- | --- |
| Release build | Thành công, 0 error; 8 warning dependency đã có | [build.log](evidence/learning-commerce-week6/build.log) |
| Regression unit | 99/99 Pass, 0 fail/skip | [CI TRX đã đối chiếu](evidence/learning-commerce-week6/ci-test-results.json) |
| Regression integration | 315/315 Pass, 0 fail/skip; tăng 15 case từ baseline 300 | Cùng file kết quả CI; raw TRX trong artifact của run |
| Test mới focus | 15/15 Pass trên SQL Server 2022, xác nhận lại sau commit | [journeys.log](evidence/learning-commerce-week6/journeys.log) |
| Fresh demo | SQL Server 2022 `16.0.4295.3`, 6 migration và 5 user demo; cleanup container exit 0 | [run.json](evidence/learning-commerce-week6/run.json), [SQL đối chiếu](evidence/learning-commerce-week6/database-evidence.json) |
| Browser UAT seeded | 6/6 Pass, 19 PNG, không JavaScript error | [results.json](evidence/learning-commerce-week6/browser-existing/results.json) |
| Browser UAT Learning/Commerce | 10/10 Pass, 23 PNG, không JavaScript error; desktop 1440/mobile 390 | [results.json](evidence/learning-commerce-week6/browser-learning-commerce/results.json), [CSV testcase](evidence/learning-commerce-week6/browser-testcases.csv) |
| CI GitHub Actions | Audit/build/unit/integration/artifact thành công; tải và đối chiếu candidate-sha/TRX | [Run 38529445056](https://github.com/lehs-dev/english-learning-platform/actions/runs/38529445056), [record](evidence/learning-commerce-week6/remote-ci-source.json) |
| Audit credentials | Scanner 14 self-tests Pass; 0 findings trên reachable history/worktree | [audit](evidence/learning-commerce-week6/security-audit.txt) |

Tổng regression **414 case = 99 unit + 315 integration**. 15 case focus đã nằm trong 315 integration. Browser là **16 hành trình riêng, 42 ảnh**, không cộng vào tổng xUnit. Đã xem trực quan ảnh Payment Success, progress 66,67%, owner report và mobile Archived.

Candidate browser/gate: **`ea1e4bb85d3dcc1b62155def6b1717c091f51f88`**, working tree sạch lúc chạy. Thời gian gate: 14/10/2026; giờ UTC/Asia-Saigon và thời điểm từng case có trong JSON. Source digest browser **`faae0562963c36eb927eef547ed0a7c9203702a0be7a9a957fdb0a66a8527c2c`**; [manifest theo file](evidence/learning-commerce-week6/source-manifest.json) và assembly hash nằm trong run.json.

CI nguồn đã tải artifact chạy SHA **`8c78d49bd7dea32600533c8e466c61c6b548f2d0`**. Giữa SHA đó và candidate browser chỉ sửa script QA xử lý JSON Windows PowerShell 5; production source/test C# không đổi. Bằng chứng CI giữ đúng SHA của run, không đổi metadata để giả thành SHA khác. Commit đóng gói hồ sơ sau candidate chỉ bổ sung tài liệu/evidence; CI của nhánh tiếp tục chạy tự động khi push.

Đối chiếu database sau UAT: Free có 0 Order/Payment, 1 Enrollment, progress hiệu lực 1/2 và 1 mốc hoàn thành lịch sử. Paid có 2 Order, 1 Payment Failed, 1 Payment Succeeded, tổng success 199000 VND, 1 Enrollment, progress 1/3 và 1 mốc lịch sử. Cả hai Course Archived. Các giá trị này phản ánh bước Incomplete/publish thêm bài trước Archive; không sửa điểm/timestamp để tạo bằng chứng.

Những lỗi trong harness đã sửa trước lần verified: đọc ID nằm trong path thay vì query; readiness native-command error khi SQL đang khởi động; quote template Docker trong PowerShell; tham số sqlcmd `-h/-y` không tương thích; ConvertFrom-Json trong Windows PowerShell 5 trả array như một pipeline object. Các lỗi này thuộc code QA mới. Các run thử lỗi được giữ trong artifacts local và không dùng làm kết quả Passed.

Kết luận kỹ thuật: các tiêu chí Learning/Commerce trong matrix đạt trên SQL Server 2022 và trình duyệt đã kiểm chứng. Không phát hiện lỗi production mới trong phạm vi này. Hồ sơ có expected/actual, dữ liệu đối chiếu và ảnh để nhóm dùng cho báo cáo/demo.

Baseline local SQL Server 2025 (`17.0.1000.7`): build thành công, 99 unit Pass; integration 298/300 Pass, 2 timeout tại `AssessmentQueryServiceTests.OverviewAndTakingSheet_RejectUnverifiedEnrollmentAndMismatchedCourseOwnership` cho WrongCourse và FailedPayment. Đây là baseline trước thêm test và thuộc query Assessment hiện có. Cả hai case đã Pass trong regression SQL Server 2022 chuẩn của repo; hiện tượng chưa tái hiện trên môi trường chuẩn. Source Assessment không được sửa trong nhiệm vụ này.

Restore/build hiện có cảnh báo NU1902/NU1903 cho dependency `System.Security.Cryptography.Pkcs 9.0.11`. Không thay package của nhóm trong đợt QA này; ghi issue cho owner dependency để nâng phiên bản và kiểm thử riêng. Build/test Pass không đồng nghĩa đã giải quyết cảnh báo dependency.

UAT trong hồ sơ là automation; chưa có chữ ký nghiệm thu hoặc buổi rehearsal của nhóm được ghi thay bằng automation.
