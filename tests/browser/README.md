# Browser UAT

## Popup đăng nhập và đăng ký

Sau `npm ci` và `npx playwright install chromium`, chạy `npm run test:auth-modal`.
Suite chạy tuần tự với một Chromium, dùng dialog/aside, CSS và JavaScript production
cùng form HTML giữ tên trường MVC và CSRF. Các case kiểm tra mở/chuyển form giữ URL
và ReturnUrl, lỗi xác thực, payload CSRF, đăng ký thành công, chuyển trang sau đăng nhập,
focus bàn phím, Escape/backdrop/nút đóng, URL Account mở trực tiếp và màn hình 320/390px.
Fixture giả response HTTP, không cần database; không thay thế integration tests
cho Razor, xác thực tài khoản, CSRF và chuyển hướng backend.
`PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` tùy chọn chỉ tới Chromium đã cài.
`ELP_AUTH_MODAL_SCREENSHOTS` chọn thư mục lưu ảnh fixture đăng nhập desktop và đăng ký 390px.

## Luồng Student theo từng nhóm nhỏ

Từ thư mục gốc, `bash scripts/test-student-flow.sh --group auth` build bằng một
MSBuild worker rồi chạy từng lớp với `tests/low-memory.runsettings`. Các nhóm
khác: `catalog`, `learning`, `practice`, `checkout`, `assessment`, `completion`.
Thêm `--no-build` sau khi đã build source mới nhất; `--list` chỉ xem lớp được chọn.
Mặc định script chạy các nhóm .NET tuần tự, không chạy một suite lớn song song;
lớp dùng chung giữa các nhóm chỉ chạy một lần. `ELP_TEST_SQLSERVER` có thể trỏ tới
server local, nhưng fixture luôn tạo và dọn database GUID riêng.

Sau khi cài dependencies/Chromium, chạy fixture autosave riêng:

```bash
node --test --test-concurrency=1 tests/browser/assessment-ui.test.mjs
```

Fixture dùng tên `Answers[n]`, question IDs, CSRF và `Answers.Index` giống Razor;
kiểm tra payload sparse, lưu lỗi/thử lại, hai tab, hàng đợi, xác nhận nộp và hết giờ.

`student-flow-uat.mjs` kiểm tra UI thật trên **app Development/database mới riêng**
đã bật `LearningDemo:Enabled` và `CheckoutSandbox:Enabled`. Mật khẩu seed chỉ
truyền qua environment; không chạy lên database đang làm việc. Mỗi nhóm tạo
Student mới bằng form, không sửa SQL hoặc giả response. Chạy từng lệnh riêng:

```bash
export ELP_STUDENT_UAT_ISOLATED=true
export ELP_UAT_URL=http://127.0.0.1:<port-app-test>
export ELP_UAT_RESULTS=TestResults/student-browser/auth
# LearningDemo__Password phải có trong environment và khớp demo seed.
node tests/browser/student-flow-uat.mjs --group auth
```

Đổi nhóm và thư mục kết quả lần lượt thành `learning`, `practice`, `assessment`,
`checkout`, `responsive`. Suite chỉ nhận URL loopback và cờ môi trường cách ly.
Mỗi lượt dùng một Chromium, đóng context sau nhóm; hai tab Assessment cùng context
kiểm tra autosave từ index khác 0 và giữ đáp án của tab còn lại. Learning đi tới
Final và trạng thái hoàn thành khóa; Assessment có audio nội bộ, resume, điểm và
quyền riêng tư; responsive kiểm tra 320/390px. JSON ghi hash source trước/sau và
ảnh không chụp trang Account. Caller chịu trách nhiệm dọn app/database test.

## Ba loại Assessment trên web thật

`npm run test:assessment-types` mở Chromium và thao tác form của ứng dụng thật:
tạo/sửa Draft, chọn câu hỏi, Preview, Publish, Student Start/Submit và kết quả
của PlacementTest, SkillAssessment, PracticeExam. Có kiểm tra cấu hình sai không
ghi đè dữ liệu/version, câu trái TargetSkill, PassingScore trống/0/100 và giao diện
1440/390px. Suite không dùng HTML fixture hoặc bỏ qua đăng nhập/CSRF.

Chỉ chạy trên app/database riêng mới seed, không dùng database đang làm việc.
Caller phải bật demo seed bằng mật khẩu tạm qua environment và đợi `/health` đạt:

```powershell
$env:ELP_T2_UAT_ISOLATED = 'true' # Chỉ xác nhận sau khi đã tạo môi trường riêng.
$env:ELP_UAT_URL = 'http://127.0.0.1:<port-app-test>'
$env:ELP_UAT_RESULTS = 'D:\english-learning-platform\TestResults\t2-browser-uat'
# LearningDemo__Password phải có trong environment và trùng mật khẩu seed.
npm run test:assessment-types
```

Script từ chối URL không phải loopback hoặc thiếu xác nhận môi trường riêng.
`PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` tùy chọn chỉ tới Chromium đã cài.
Ảnh và `assessment-types-results.json` được lưu trong thư mục kết quả; không lưu
cookie, token, storage state hoặc mật khẩu. Caller chịu trách nhiệm dọn môi trường test.

## Phạm vi Independent/Course-linked trên web thật

`npm run test:assessment-scope` kiểm tra Teacher gắn/chuyển/gỡ Course, scope trên
danh sách/Preview và quyền Student thay đổi tương ứng. Có kiểm tra tab cũ, form
Start cũ, Course/Assessment khác owner, khóa sau Attempt và Final không tự bị gỡ.
Ảnh desktop/mobile và `assessment-scope-results.json` được ghi vào thư mục kết quả.

Dùng app/database riêng mới seed như trên, đặt `ELP_T3_UAT_ISOLATED=true` thay cho
cờ T2; các biến URL, mật khẩu seed, thư mục kết quả và Chromium dùng cùng quy ước.
Script không thử giao dịch thanh toán; Pending/Failed/verified payment và race
Start/đổi scope được kiểm tra riêng bằng integration tests trên SQL Server.

## Kiểm thử responsive và điều hướng theo vai trò

Sau khi cài dependencies và Chromium như bên dưới, chạy `npm run test:roles`.
Suite chạy tuần tự, dùng chung một Chromium và đóng từng browser context để giảm RAM.
Có thể chia nhỏ thêm theo vai trò, ví dụ:

```bash
node --test --test-concurrency=1 --test-name-pattern='student' role-layouts.test.mjs
node --test --test-concurrency=1 --test-name-pattern='teacher' role-layouts.test.mjs
```

Mười test dùng HTML lấy từ bốn Razor layout thật cùng CSS và `site.js` production,
kiểm tra menu, focus bàn phím, backdrop, Escape, resize và tràn ngang ở
320/390/768/900/901/1024/1025/1080/1081/1250/1440px cùng drawer ở chiều cao 320px. Fixture bỏ cú pháp
Razor, lấy đúng nhánh guest/authenticated của AccountMenu thật với tên ổn định,
giữ notification/chevron và chèn cards/form/table đại diện. Suite không cần database và
không xác minh việc render Razor, phân quyền hay dữ liệu backend; các phần đó
được kiểm tra bằng integration tests và UAT với ứng dụng thật.

## Kiểm thử giao diện Attempt riêng

Sau `npm ci` và `npx playwright install chromium`, chạy `npm run test:assessment`.
Mười một test dùng Chromium thật với HTML fixture và script `assessment.js` của ứng dụng:
xác nhận/hủy nộp với câu bỏ trống, lưu thủ công, hết deadline khi dialog đang mở,
và nộp đáp án mới nhất sau lỗi autosave. Bổ sung kiểm tra thứ tự tự lưu,
chỉ báo đã lưu sau xác nhận của server cho lựa chọn mới nhất, phục hồi sau lỗi
và cảnh báo rời trang khi còn đáp án chưa lưu. Điều hướng số câu/Câu trước/Câu tiếp
phải giữ lựa chọn mà không gửi form; xác nhận nộp khi autosave còn chờ phải giữ modal,
chặn Escape/bấm lặp và chỉ gửi một lần với đáp án mới nhất. Hết giờ phải khóa tương tác
mà không mất payload; trang mở lại lượt quá hạn tự gửi POST có CSRF để chấm lượt cũ.
Các test này không cần database;
suite `npm test` bên dưới kiểm tra giao diện thực tế cùng backend.

Chạy trên **database demo riêng mới seed**, không dùng database đang có dữ liệu thật. Suite không reset dữ liệu. Bốn nhóm actor, sáu case (hai Student và thêm kiểm tra free enrollment/privacy), 19 ảnh gồm viewport 390px.

```bash
cd tests/browser
npm ci
npx playwright install --with-deps chromium
# Nhập password đã dùng để seed; không ghi literal vào shell history.
read -r -s -p 'Demo password: ' LearningDemo__Password
export LearningDemo__Password
export ELP_UAT_URL=http://localhost:8081
npm test
unset LearningDemo__Password
```

`ELP_UAT_RESULTS` chọn thư mục ảnh/report (mặc định `TestResults/browser-uat` tương đối với thư mục chạy). Không ghi cookie, storage state hoặc mật khẩu vào artifacts. `results.json` ghi từng case, giờ UTC/múi giờ Asia/Saigon, SHA và trạng thái working tree; local run kiểm tra nguồn không thay đổi trong khi chạy.

Trong Docker có thể chạy bằng image `mcr.microsoft.com/playwright:v1.64.0-noble` trên cùng Compose network, URL `http://web:8080`. Sao chép các file suite vào runner và chạy `npm ci`, `node uat.mjs`. Caller phải truyền `ELP_UAT_CANDIDATE`, `ELP_UAT_CLEAN`, `ELP_UAT_SOURCE_DIGEST`, `ELP_UAT_IMAGE_ID` từ bản nguồn và image đã kiểm tra. Đối chiếu label `org.opencontainers.image.revision` với SHA trước run. Không dùng SHA base làm SHA của source chưa commit.

Suite thực thi các bước qua Chromium, kiểm tra CSRF, paid/free access, resume giữ deadline/answers, 80% score, completion, ownership và Admin isolation. Đây là **automated browser UAT**, không thay chữ ký nghiệm thu hay rehearsal của thành viên.
