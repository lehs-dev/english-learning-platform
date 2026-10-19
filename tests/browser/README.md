# Browser UAT

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
Chín test dùng Chromium thật với HTML fixture và script `assessment.js` của ứng dụng:
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
