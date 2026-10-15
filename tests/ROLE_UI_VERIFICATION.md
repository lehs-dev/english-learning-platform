# Kiểm thử UI theo vai trò với ít RAM

## Kết quả Milestone 4–5 — 15/10/2026

- Build thành công: 0 lỗi, 0 cảnh báo.
- Unit: **274/274**, chia thành 20 lớp chạy lần lượt.
- Integration: **690/690**, chia thành 36 lớp chạy lần lượt; các lớp bị ảnh
  hưởng bởi sửa layout/hồ sơ được chạy lại sau build cuối.
- Browser: **14/14** — 10 test responsive/navigation và 4 test assessment.
- Ứng dụng demo thật: **218/218 kiểm tra**, gồm chuyển hướng đăng nhập, layout
  của Guest/Student/Teacher/Admin, hồ sơ, preview khóa học của Teacher, menu
  mobile, tài khoản và tràn ngang. Đây là các kiểm tra UI, không phải 218 test
  nghiệp vụ độc lập.
- Viewport: 320, 390, 768, 900, 901, 1024, 1025, 1080, 1081, 1250, 1440px;
  thêm 390×320px để kiểm tra sidebar và menu tài khoản khi màn hình thấp.

Các tiến trình kiểm thử chạy tuần tự; build dùng một worker, các fixture browser
dùng một Chromium và đóng từng context. SQL test dùng server sẵn có với database
GUID riêng. Phiên kiểm tra ứng dụng thật dùng database demo tạm riêng; tiến trình
web đã dừng và database demo đã xóa sau khi chạy. Không chạy kiểm thử vào database
ứng dụng hiện có.

Kết quả được kiểm tra trên working tree chưa commit. Log/TRX, hai bảng tổng hợp,
JSON của ứng dụng thật và tám ảnh nằm trong thư mục local được gitignore:

- [Unit summary](../TestResults/role-ui-final/unit/summary.tsv)
- [Integration summary](../TestResults/role-ui-final/integration/summary.tsv)
- [Browser responsive](../TestResults/role-ui-final/browser-roles.log)
- [Browser assessment](../TestResults/role-ui-final/browser-assessment.log)
- [Ứng dụng thật và danh sách ảnh](../TestResults/role-ui-final/live-browser/results.json)

Browser fixture kiểm tra DOM layout đã bỏ cú pháp Razor; phiên ứng dụng demo
kiểm tra Razor render thật. Chưa chạy lại toàn bộ các script UAT giao dịch có
thay đổi dữ liệu trong `uat.mjs`/`learning-commerce-uat.mjs`; các integration test
nghiệp vụ tương ứng nằm trong 690 test đã qua.

## Chạy lại theo nhóm nhỏ

Runner chia .NET thành từng lớp kiểm thử: lớp hiện tại kết thúc và giải phóng
fixture trước khi mở lớp tiếp theo. Build dùng một worker, tắt compiler server;
`low-memory.runsettings` tắt chạy đồng thời các assembly và collection xUnit.
Runner lấy danh sách lớp từ test discovery, nên lớp partial chỉ chạy một lần và
các lớp mới tự được đưa vào danh sách.

```bash
# Xem danh sách lớp; bước này build/discover nhưng chưa chạy kiểm thử.
bash scripts/test-role-ui.sh --list

# Chạy unit trước, integration sau; mỗi lần chỉ một lớp.
bash scripts/test-role-ui.sh --suite unit
bash scripts/test-role-ui.sh --suite integration

# Kiểm tra riêng từng nhóm trong lúc sửa UI; tái dùng Debug binaries vừa build.
bash scripts/test-role-ui.sh --suite integration --class RoleLayout --no-build
bash scripts/test-role-ui.sh --suite integration --class RoleBasedSmartRouting --no-build
bash scripts/test-role-ui.sh --suite integration --class LandingPage --no-build

# Browser fixture: Chromium thật, không cần tài khoản hoặc database.
# Cài npm ci và Playwright Chromium trong tests/browser trước lần đầu.
bash scripts/test-role-ui.sh --suite browser

# Toàn bộ .NET rồi browser fixture, vẫn tuần tự.
bash scripts/test-role-ui.sh --browser
```

Nếu đã cấu hình `ELP_TEST_SQLSERVER`, runner giữ nguyên biến này. Mỗi fixture tạo
database tên GUID và chỉ xóa database do chính fixture tạo. Không có biến này thì
Testcontainers dùng Docker để tạo/hủy SQL Server cho lớp hiện tại. Runner không
tạo, ghi hoặc in chuỗi kết nối. Cần có sẵn SQL Server phù hợp hoặc Docker.

Mặc định dừng ở lỗi đầu tiên; `--keep-going` chạy tiếp các lớp còn lại theo thứ tự.
Mã thoát `0` là toàn bộ nhóm đã chọn thành công, `1` là lỗi build/discovery/test,
`2` là tham số không hợp lệ hoặc không có lớp khớp bộ lọc. `--list` trả `0` khi
liệt kê thành công và không xác nhận rằng các test đã chạy.

Mỗi lần chạy ghi log build/discovery, danh sách lớp, TRX riêng từng lớp và
`summary.tsv` trong thư mục `TestResults/role-ui.*`. Dùng `--results PATH` để chọn
thư mục riêng. TRX phải có ít nhất một test cho mỗi lớp mới được ghi nhận thành
công; không coi bộ lọc rỗng là thành công.

`--browser` và `--suite browser` chỉ chạy `tests/browser/*.test.mjs`, lần lượt
từng file với `--test-concurrency=1`. Các hành trình UAT có tài khoản/database
demo trong `uat.mjs` và `learning-commerce-uat.mjs` có hướng dẫn riêng ở
[browser/README.md](browser/README.md).
