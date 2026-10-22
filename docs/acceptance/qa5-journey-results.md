# QA5 — UAT xuyên suốt Practice → Assessment → Result

Ngày kiểm tra: **21/10/2026, Asia/Bangkok**.

**Kết luận: đạt kịch bản QA5 đã giao.** Lượt browser cuối có **6/6 kịch bản
pass, 0 lỗi JavaScript, 48 ảnh desktop/mobile**. Hồi quy **390 unit + 151
integration case pass**, build Release 0 warning/error. Chưa commit/push.

## 1. Môi trường và phiên bản thực sự được kiểm tra

- App riêng: `http://127.0.0.1:18085`; SQL Server 2022 container riêng, bind
  loopback `15055`, database `ELP_QA5`; kho audio tạm riêng. Không dùng app
  `8080`, database, volume hoặc audio đang làm việc của người dùng.
- Startup chạy migration và demo seeder hiện có để dựng 5 tài khoản, Course
  mẫu và Enrollment free ban đầu. Toàn bộ Stimulus, 5 Question của QA5, Practice,
  Assessment, answers và kết quả kiểm tra **đều tạo qua UI**, không sửa bằng SQL.
  Lệnh SQL duy nhất trong runner là `SELECT 1` để kiểm tra SQL Server sẵn sàng.
- Teacher1 tạo nội dung, Teacher2 đối chứng quyền Preview; Student1 có Enrollment
  free hợp lệ làm bài. Mật khẩu mỗi lượt sinh ngẫu nhiên trong environment,
  không ghi `.env`, cookie, storage state, token hoặc mật khẩu vào report.
- Chromium `156.0.8078.4`, Playwright `1.64.0`, Node `v24.14.0`, Windows.
  Viewport **1440×1000** và **390×844**, ảnh full-page. Chặn media/font từ bên
  thứ ba; passage và audio sử dụng tài nguyên nội bộ của chính app.
- Mốc Git gốc: `3f45ac1501c05db4b7a6b70975c37c1add05e8c2`; working tree dirty,
  gồm thay đổi QA2–QA5 chưa commit. Không gọi SHA gốc là SHA candidate mới.
- Lượt chốt: `QA5-26603160`, **19:04:04–19:04:29 (+07:00)**.
  Source digest: `3f43cb2d99bf4345196ec1a2a31a6b6ba0164ad8371cc33bd2515459003f5e21`.
  Script hash cả tracked/untracked files trong `src/`, `tests/browser/` và runner
  QA5, kiểm lại sau run; không có source thuộc phạm vi hash thay đổi trong lúc chạy.

Artifact chốt: `TestResults/qa5/browser-run-06/qa5-results.json` và 48 PNG cùng thư
mục. Đây là **automated UAT bằng browser thật + xem lại ảnh**, không phải HTML
fixture hoặc chữ ký nghiệm thu/rehearsal của cả nhóm. `TestResults/` được Git
ignore; thành viên khác chưa tự có các artifact này trên máy.

## 2. Preview Practice đã bổ sung theo yêu cầu

Trong lúc rà QA5, chưa có Preview Practice riêng. Sau khi người dùng chọn bổ
sung, đã thêm:

| Vị trí | Chức năng / lý do |
| --- | --- |
| [TeacherPracticePreview.cs](../../src/EnglishLearningPlatform.Application/Practice/TeacherPracticePreview.cs) | Contract và DTO chỉ đọc, tái sử dụng `PracticeQuestionSheet`; không có đáp án đúng, giải thích hoặc kết quả Student. |
| [TeacherPracticePreviewService.cs](../../src/EnglishLearningPlatform.Infrastructure/Practices/TeacherPracticePreviewService.cs) | Teacher Active đúng một role, đúng owner; kiểm cả Course/Question/Stimulus. Draft/Published được xem, Draft thiếu parent/câu có cảnh báo. Sắp xếp câu/option theo dữ liệu đã lưu, chỉ trả audio URL an toàn; `AsNoTracking`, không gọi SaveChanges/Start/Submit/Publish. |
| [TeacherPracticePreviewController.cs](../../src/EnglishLearningPlatform.Web/Controllers/TeacherPracticePreviewController.cs) | Chỉ GET; actor lấy từ claims, 404/Forbid đúng theo service; NoStore. |
| [Preview view](../../src/EnglishLearningPlatform.Web/Views/TeacherPracticePreview/Index.cshtml) | Cấu hình, passage/audio, lựa chọn thử và điều hướng. Không có form làm bài hoặc script lưu đáp án; F5 bỏ lựa chọn thử. Tái sử dụng CSS câu hỏi hiện có. |
| [DI](../../src/EnglishLearningPlatform.Infrastructure/DependencyInjection.cs), [trang soạn](../../src/EnglishLearningPlatform.Web/Views/TeacherPracticeQuestions/Index.cshtml), [danh sách](../../src/EnglishLearningPlatform.Web/Views/TeacherPractices/Index.cshtml) | Đăng ký service và thêm nút Preview. Danh sách đặt nút ở cột Thao tác riêng, giữ nguyên nội dung các cột dữ liệu cũ. |

7 case mới trong [TeacherPracticePreviewTests.cs](../../tests/EnglishLearningPlatform.IntegrationTests/TeacherPracticePreviewTests.cs)
kiểm Draft/Published, thứ tự, passage/audio, HTML encoding, GET/POST và quyền
owner/role, liên kết sai owner, Draft rỗng. Preview ba lần so sánh snapshot
Practice/Question/Stimulus/Option/PracticeSubmission/Answer và count Attempt/result;
không chỉ dựa vào số hàng hoặc việc không có nút Submit trên UI.

## 3. Kịch bản, expected và actual

Mỗi lượt runner tạo môi trường mới; prefix lượt chốt là `QA5-26603160`.
Ba Reading dùng chung passage về Alex đến thư viện vào thứ Bảy; hai Listening
dùng chung WAV tone 220 Hz một giây được upload bằng form Teacher.
Tone dùng kiểm tra upload/authorization/playback, không phải đánh giá nghe hiểu
giọng nói hoặc chứng nhận chất lượng nội dung sư phạm.

| Mã browser case | Thao tác / expected | Actual lượt chốt |
| --- | --- | --- |
| QA5-01 | Teacher tạo passage/audio, tạo 5 Question đúng một đáp án, tìm/lọc và mở Detail. Passage trống báo lỗi; audio nội bộ thực sự phát được. | **PASS**: 3 Reading + 2 Listening được tạo qua form; tìm đúng 5; Detail passage/audio đúng. `audio.play()` chạy, readyState đủ dữ liệu và currentTime tăng; không chỉ kiểm tra có thẻ audio. |
| QA5-02 | Tạo Practice gắn Lesson của Course mình, thêm 5 câu, lưu/reload; bấm nút Preview Draft, Publish rồi Preview Published. | **PASS**: giữ đúng QuestionId và thứ tự; cấu hình/passage/2 player hiển thị. Bấm chọn trong Preview rồi F5 không giữ lựa chọn; Teacher2 mở URL bị 404. |
| QA5-03 | Student làm Practice 2 lần, lần đầu 3/5 đúng, lần hai 5/5 đúng; xem đáp án/giải thích và kết quả gần nhất. | **PASS**: 60% → 100%; mỗi lần có 5 feedback và 5 giải thích. Mở lại cùng Result URL thấy 100%; Dashboard chỉ một mục Practice hiện tại 100%. Invariant DB latest-only được chứng minh thêm bởi QA2, không suy ra chỉ từ một thẻ UI. |
| QA5-04 | Teacher tạo PracticeExam Draft, thử Publish rỗng; chọn 5 câu, đặt points 1/3/8/5/3, Preview, gắn Course và Publish. | **PASS**: thiếu câu hiện lỗi server, không tự Publish; reload giữ points. Preview Draft/Published đều hiển thị đúng, không Start Student. Đề mới Course-linked, không thay Final mẫu. |
| QA5-05 | Student Start, autosave lựa chọn; điều hướng, đóng tab, đăng nhập phiên browser mới rồi Resume/F5; hủy xác nhận một lần rồi Submit. Làm đủ 3 lượt. | **PASS**: cùng Attempt trong form Submit, cùng deadline và các lựa chọn đã lưu; không tự tiêu thụ lượt do F5. Kết quả tức thì lần lượt 60%, 85%, 70%. |
| QA5-06 | History, từng Result, Dashboard/Profile; representative highest; kiểm nội dung riêng tư và desktop/mobile. | **PASS**: đủ 3 lượt riêng; chọn lượt 85%, Reading đi cùng là 75%, không lấy Reading 91,67% của lượt 70%. Result chỉ có Reading đủ 3 câu; Listening 2 câu không hiển thị. Không trả option IDs, `SelectedOptionId`, `IsCorrect`, marker giải thích hoặc đáp án từng câu trong Result. |

Tính tay cho Assessment, tổng points = 20; Reading gồm câu 1–3, tổng 12 points:

| Lượt | Các câu đúng | Overall | Reading |
| --- | --- | ---: | ---: |
| 1 | 1, 2, 3 | 12/20 = 60% | 12/12 = 100% |
| 2 | 1, 3, 4, 5 | 17/20 = 85% | 9/12 = 75% |
| 3 | 2, 3, 5 | 14/20 = 70% | 11/12 = 91,67% |

PassingScore=80: lượt 85% Pass, hai lượt còn lại Fail. Course progress là tiến độ
Lesson, khác điểm Assessment; kịch bản không Mark Complete Lesson nên Dashboard
vẫn có thể hiện 0/1 Lesson dù điểm đại diện Assessment là 85%.

## 4. Lỗi thấy khi thực sự chạy và cách xử lý

- Integration đầu `qa5-practice-preview.trx`: 69/83 pass, 14 fail vì nút Preview
  ban đầu nằm trong ô tên, làm ô có thêm chữ Preview. Đã chuyển nút sang cột
  Thao tác riêng, **không đổi expected test danh sách cũ**; lượt sau 83/83 pass.
- Browser run 01–03 giữ nguyên report lỗi: script tưởng Stimulus mới tự được
  chọn, tưởng Question Bank là bảng thay vì card, rồi tìm tên Practice trong
  anchor “Xem lại bài giải”. Đã sửa locator theo source/HTML thật, không sửa app
  để hợp với giả định test. Resume cũng kiểm Attempt từ form POST, không bắt URL
  luôn phải có query `attemptId` khi controller render Resume ngay.
- Run 04/05 đã 6/6 pass, nhưng xem ảnh thấy bảng History/Skill trên mobile ép
  chữ/ngày/nút thành từng ký tự. Đã sửa **riêng `.assessment-table` và wrapper**
  trong [assessment.css](../../src/EnglishLearningPlatform.Web/wwwroot/css/assessment.css):
  min-width 560px, cuộn ngang trong wrapper, không bẻ chữ tùy ý, nút giữ nguyên
  dòng. Wrapper History/Result có role/label và tabindex để focus bằng bàn phím.
  Không sửa logic chấm, query hoặc quyền truy cập của Assessment.
- Run 06 kiểm thêm khung bảng focus được, thực sự cuộn ngang được và chụp cả đầu
  trái/phải. Đã xem lại ảnh History mobile sau sửa; ngày giờ và nút đọc được.
  Những màn hình khác được kiểm main visible, không tràn ngang document, thao tác
  chọn/điều hướng và đóng/mở sidebar Teacher ở mobile. Không tuyên bố pixel-perfect
  hoặc chứng nhận WCAG/browser engine khác.
- Audit từng báo một false positive ở biểu thức sinh mật khẩu demo, không phải
  password literal. Đã tách mật khẩu ngẫu nhiên thành biến tham chiếu rõ ràng;
  audit cuối 0 finding, không thêm allowlist để bỏ qua mật khẩu thật.

## 5. Kết quả test và artifact

| Kiểm tra | Kết quả / artifact local |
| --- | --- |
| Build Release source cuối | PASS, 0 warning / 0 error. |
| Unit | PASS **390/390**, 0 fail/skip; `TestResults/qa5/qa5-unit.trx`. |
| Preview/Practice regression ban đầu sau sửa cột | PASS 83/83: Preview mới 7 + Questions 24 + Index web 52; `qa5-practice-preview-final.trx`. |
| Hồi quy HTTP sau CSS/ARIA cuối | PASS **151/151**, 0 fail/skip; `TestResults/qa5/qa5-regression.trx`: 83 case trên + AssessmentUiFlow 24 + AssessmentQueryService 44. |
| Browser chốt | PASS **6/6**, 0 pageerror; 48 PNG và `browser-run-06/qa5-results.json`. |
| Audit source | 0 finding; `TestResults/qa5/security-audit.csv`; chỉ working tree, không quét Git history. |
| Dọn môi trường | Không còn container label `elp.qa5=isolated`, app/private ports hoặc audio demo tạm của runner; giữ nguyên artifact. |

Không cộng 83 với 151 hoặc các lượt browser lặp thành testcase độc lập. Không
lấy 435 case QA4 làm kết quả chạy của code Preview mới. File log/JSON/TRX trong
thư mục QA5 cũng được rà mẫu connection string/password, cookie và token; không
phát hiện file khả nghi. Không xuất mật khẩu hoặc state phiên đăng nhập.

Ảnh tiêu biểu trong `TestResults/qa5/browser-run-06/`:

- `practice-preview-published-1440.png`, `practice-preview-published-390.png`.
- `practice-result-60-390.png`, `practice-result-100-390.png`.
- `assessment-taking-saved-1440.png`, `assessment-resumed-390.png`.
- `assessment-result-85-390.png`, `assessment-result-85-390-table-end.png`.
- `assessment-history-390.png`, `assessment-history-390-table-end.png`.
- `dashboard-1440.png`, `profile-390.png`.

## 6. Chạy lại và giới hạn nghiệm thu

Sau khi Docker Engine chạy và dependencies browser hiện có đã cài:

```powershell
dotnet build --configuration Release
pwsh -NoProfile -File scripts/qa5-browser-uat.ps1 -RunName browser-retest-01
```

Chọn RunName mới để không ghi đè bằng chứng. Runner từ chối khi port riêng đã
bị dùng, không dừng app người khác; tự tạo mật khẩu tạm và tự dọn app/container/
audio sau chạy. SQL và dữ liệu demo tạm bị xóa, không giữ để vào lại web; ảnh và
report vẫn còn. Có thể chạy lại để tái tạo bằng các thao tác UI như trên.

**Có thể tick QA5 theo phạm vi người dùng đã giao.** Các test payment/race/timeout
sâu đã có QA2–QA4, không nhận là được browser QA5 kiểm lại. Suite này không chạy
toàn bộ 13 dòng UAT thủ công mở rộng ở A1: paid checkout, offline retry, chờ hết
deadline, Course Final và Clone vẫn cần biên bản riêng nếu nhóm yêu cầu rehearsal
những phần đó. QA6 đóng gói báo cáo/demo, CI/PR và chữ ký nghiệm thu chưa hoàn thành.

Không có migration mới hoặc sửa dữ liệu demo bằng tay. Chưa fetch/merge main,
commit/push hoặc tạo PR trong QA5.
