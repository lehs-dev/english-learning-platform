# Hồ sơ E2E và diễn tập của Dương

Ngày chuẩn bị: 23/10/2026. Nhánh `test/duong-e2e-handover`.
Phần này bổ sung cho hồ sơ team, không nhận lại công việc đã có là mới viết.

## File dùng để đọc và trình bày

- `duong-e2e-handover.docx`: báo cáo và hướng dẫn bốn role, kịch bản demo, bảng diễn tập; đã render kiểm tra bốn trang.
- `duong-e2e-demo.pptx`: tám slide có speaker notes; đã kiểm tra cấu trúc, import và render từng slide.
- `evidence-20261023.zip`: report JSON, 35 ảnh, metadata môi trường và checksum của lượt đạt cuối.
- `results.md` và `user-guide.md`: nguồn nội dung dễ review và chỉnh sửa.

## Công việc thực hiện

- Dựng project Docker mới `elp-qa6-duong-20261023`: SQL, migration và kho audio riêng.
- Thêm `-EnablePaymentSandbox` cho launcher QA6; mặc định vẫn tắt thanh toán sandbox.
- Dùng lại sáu nhóm Student của team: auth, learning, practice, assessment, checkout, responsive.
- Bổ sung kiểm tra Guest/Teacher/Admin theo giao diện hiện tại và runner tổng hợp tám nhóm.
- Sửa lỗi responsive Admin phát hiện qua E2E: trang User rộng 613px tại viewport 390px.
- Chuẩn bị hướng dẫn sử dụng, báo cáo và nội dung diễn tập riêng cho Dương.

## Chạy lại từ môi trường mới

Mở Docker Desktop, chờ Engine running. Trong thư mục gốc repo:

```powershell
# Chọn tên project mới; launcher không xóa hoặc reset project đã có.
& ./scripts/qa6-start-demo.ps1 -ProjectName elp-qa6-duong-rehearsal02 -EnablePaymentSandbox
```

Nhập kín hai mật khẩu khác nhau cho SQL và demo. Web ở `http://localhost:18080`,
SQL ở cổng 15433. Không dùng mật khẩu thật của tài khoản cá nhân hoặc đưa secret vào terminal log.
Nếu cổng bận, chọn project và cổng khác bằng `-WebPort`/`-SqlPort`.
Lệnh PowerShell trên đã chạy bằng Windows PowerShell; không bắt buộc phải gõ `pwsh`.

Sau khi `/health` trả 200, cài Playwright nếu chưa có (`npm ci` và
`npx playwright install chromium` trong `tests/browser`). Ở thư mục gốc:

```powershell
$demoCredential = Read-Host 'Mat khau demo vua seed' -AsSecureString
$env:LearningDemo__Password = [System.Net.NetworkCredential]::new('', $demoCredential).Password
$env:ELP_STUDENT_UAT_ISOLATED = 'true'
& ./scripts/duong-e2e.ps1 -BaseUrl http://127.0.0.1:18080 -RunName duong-rehearsal02
Remove-Item Env:LearningDemo__Password
```

Chỉ đặt cờ ISOLATED sau khi đã dựng môi trường riêng. Runner tạo tài khoản và dữ liệu thử,
không reset DB. Mỗi lần dùng RunName mới để giữ bằng chứng; launcher fresh cũng cần tên project mới.

## Bản đã dựng trên máy Dương

- Web: `http://localhost:18080`; SQL: 15433. Không ảnh hưởng web chính 8080.
- Mật khẩu local trong `.env.duong-e2e` (Git bỏ qua). Dùng giá trị `LearningDemo__Password`
  để đăng nhập các tài khoản ở hướng dẫn sử dụng; không dùng mật khẩu SQL.
- Giữ database/audio sau test để Dương tự diễn tập. Không chạy `down -v` hoặc prune.
- Các test Student tự tạo tài khoản mới để không lệ thuộc số lượt thi của tài khoản seed.

## Bằng chứng

Lượt tổng hợp: `TestResults/duong-e2e/duong-20261023-final/`.
`run.json` ghi kết quả nhóm và commit gốc, các report con ghi ca thực tế;
`SHA256SUMS.txt` dùng kiểm tra file bị thay đổi sau chạy. Nguồn có thay đổi chưa commit,
không gán commit gốc là toàn bộ code đã kiểm thử; xem source digests trong report Student.
Các file TestResults được Git bỏ qua. Chọn ảnh/report đã rà secret nếu cần đính kèm PR.

Lần chạy ban đầu ở `TestResults/duong-e2e-20261023/` giữ cả lỗi của UAT cũ và
lỗi responsive Admin. Không thay hoặc xóa bằng chứng thất bại để nhận tất cả đều đạt.
UAT cũ `uat.mjs` còn selector và giả định seed cũ; runner tổng hợp này dùng
`student-flow-uat.mjs`, `handover-roles-uat.mjs` và `admin-uat.mjs`.

## Việc Dương cần tự làm trước nghiệm thu

Đọc hướng dẫn sử dụng, tự thực hiện kịch bản demo, nghe audio, đối chiếu kết quả
và giải thích quyền truy cập. Ghi ngày, người tham gia và actual vào bảng diễn tập.
Automated E2E không đồng nghĩa đã có chữ ký UAT của team hoặc đã chạy CI GitHub.
Không tự sửa tên tác giả tài liệu team thành Dương; chỉ ghi phần đóng góp thực tế.
