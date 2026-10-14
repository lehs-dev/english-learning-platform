# Browser UAT

## Kiểm thử giao diện Attempt riêng

Sau `npm ci` và `npx playwright install chromium`, chạy `npm run test:assessment`.
Bốn test dùng Chromium thật với HTML fixture và script `assessment.js` của ứng dụng:
xác nhận/hủy nộp với câu bỏ trống, lưu thủ công, hết deadline khi dialog đang mở,
và nộp đáp án mới nhất sau lỗi autosave. Các test này không cần database;
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
