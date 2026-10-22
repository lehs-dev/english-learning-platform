# QA6 — Testcase, báo cáo và demo

Ngày bàn giao local: **21/10/2026, Asia/Bangkok**.

## 1. Kết luận và phạm vi

**Đã chuẩn bị bộ bàn giao QA2–QA6:** testcase có actual, report lỗi/retest,
48 ảnh UAT, launcher demo Windows, hướng dẫn cấu hình/migration và kịch bản demo.
Launcher đã thực sự build app, tạo SQL/database mới, migrate/seed và đăng nhập
đủ năm actor bằng browser. Không sửa database đang làm việc hoặc code nghiệp vụ trong QA6.

Đây là bằng chứng **local**, không phải CI GitHub đã pass, chữ ký UAT của nhóm,
nghiệm thu mọi module hoặc chứng nhận đã restore backup production.
Nhóm vẫn cần lấy code đã review + artifact tương ứng, chạy rehearsal và ghi người xác nhận.

## 2. Testcase và actual đã chốt

Chi tiết điều kiện ban đầu, thao tác và expected ở [bảng A1](practice-assessment-a1-checklist.md).
Chi tiết từng testcase tự động được xuất **trực tiếp từ TRX** vào `testcase-actual.csv`
trong gói QA6, có tên test, artifact nguồn, outcome và duration. Không gán Pass cho test chưa chạy.

| Nhóm | Actual / bằng chứng chốt | Giới hạn đọc kết quả |
| --- | --- | --- |
| QA2 — Scoring/latest/no-duplicate | 8 unit, 113 integration Docker pass. Practice 33,33% →100%, giữ cùng SubmissionId; weighted Assessment, rounding/pass, history/highest và concurrent Submit có test. | Lượt local đầu111/113; retry3/3. Giữ cả report fail và retest, không đổi expected để xanh. [QA2](qa2-scoring-results.md). |
| A3 — Timeout/Resume | 390 unit,229 integration và9 browser fixture pass. Đúng từng tick deadline, Start/Submit race và Student lazy finalize HTTP được kiểm. | Fixture không phải UAT toàn app. Đường Teacher đọc/report Attempt chưa được task này triển khai. [A3](qa3-timeout-resume-results.md). |
| QA4 — Owner/Enrollment | Source cuối390 unit +435 integration pass. Save/tab cũ mất quyền, Pending/Failed, audio lịch sử, foreign Student/Teacher, Independent được kiểm có snapshot không đổi. | Giữ lượt red thật4/8 trước sửa. Quyền nghe chặn request mới, không thu hồi bytes browser đã tải hợp lệ. [QA4](qa4-owner-enrollment-results.md). |
| QA5 — Journey/browser | 6/6 live Chromium,0 JS error,48 ảnh; source cuối390 unit +151 integration pass. Practice60→100; Assessment60→85→70, đại diện85/Reading75; passage/audio và Preview riêng. | Không cộng151 với83 subset hoặc435 QA4. Hai viewport1440/390; không nhận là mọi browser/manual UAT đều pass. [QA5](qa5-journey-results.md). |
| QA6 — Setup/demo | Fresh Compose build thành công, SQL healthy, service migrate exit0; **5/5 login/role page browser pass**: Teacher1/2, Student1/2, Admin1. | Chỉ smoke launcher/seed/đăng nhập; không gọi đây là chạy lại toàn bộ integration hoặc QA5. `TestResults/qa6/demo-smoke.json`. |

### Lỗi và kết quả kiểm tra lại

| Lỗi / phát hiện | Cách xử lý | Retest |
| --- | --- | --- |
| QA2 SQL local timeout/exception | Chạy lại trên SQL2022 Docker riêng; giữ run local và retry. | 113/113 pass. Không kết luận timeout là bug chấm điểm. |
| QA4 tab cũ mất Enrollment có thể finalize sớm; audio lịch sử chưa kiểm quyền hiện tại | Save kiểm Enrollment; fallback chỉ finalize khi expired; audio kiểm quyền hiện tại. Giữ contract chấm lịch sử hợp lệ, không thay test completion cũ. | Source chốt435/435 pass. |
| QA5 nút Preview làm thay text cột tên,14 test cũ fail | Chuyển sang cột Thao tác; không sửa expected của test cũ. | 83/83, rồi151/151 sau CSS/ARIA cuối. |
| QA5 browser01–03 giả định sai dropdown/card/link | Sửa locator theo UI thật; không sửa app theo giả định. | Browser06 đủ6/6. |
| QA5 bảng mobile ép chữ/ngày/nút | CSS scoped min-width + wrapper cuộn ngang có focus. | Browser06 kiểm cuộn thực và ảnh đầu/cuối bảng. |

Các bộ test có thể giao nhau; **không cộng các lượt thành tổng số testcase duy nhất**.
13 dòng UI thủ công mở rộng ở A1 vẫn là rehearsal nhóm chưa ký, không được đổi tất cả thành Pass
chỉ từ các kết quả trên. QA5 đã chứng minh đúng sáu kịch bản browser được giao.

## 3. Phiên bản và bằng chứng

- Base commit: `3f45ac1501c05db4b7a6b70975c37c1add05e8c2`.
  Working tree có QA2–QA6 chưa commit; không gọi base SHA là commit mới chứa các sửa đổi này.
- Lượt QA5 chốt: `QA5-26603160`, 19:04:04–19:04:29 (+07), Chromium156.0.8078.4.
  Digest phạm vi runtime/browser/runner:
  `3f43cb2d99bf4345196ec1a2a31a6b6ba0164ad8371cc33bd2515459003f5e21`.
  Xem QA5 để biết phạm vi hash; QA6 chỉ thêm docs/script, không đổi source app QA5.
- Smoke QA6 là lượt riêng trên Compose `elp-qa6-verify-20261021`, port18080/15433;
  không gán timestamp/hash QA5 cho lượt này. Password ngẫu nhiên chỉ ở process/container test,
  không xuất trong report. Môi trường verify không phải demo có mật khẩu do nhóm chọn.
- Bằng chứng chốt dùng gói ZIP: `TestResults/qa6/handover-20261021-final.zip`.
  Bên trong có báo cáo, TRX đỏ/xanh đã chọn, JSON browser01–06,48PNG lượt06,
  smoke QA6, source ba script QA6, security audit, `testcase-actual.csv`, `manifest-sha256.csv`.
- Không đóng gói `.env`, connection string, DB, raw web logs, cookie hoặc browser storage.
  Script chặn các mẫu secret thường gặp trong text; audit source dùng pattern-based scanner,
  không thay thế rà soát thủ công hoặc quét mọi nguồn bí mật/remote history.

Tạo lại gói từ artifact đang có (dùng BundleName mới nếu đã tồn tại):

```powershell
pwsh -NoProfile -File scripts/qa6-package-evidence.ps1 -BundleName handover-reproduce-01
Get-FileHash TestResults/qa6/handover-reproduce-01.zip -Algorithm SHA256
```

Sau giải nén, đối chiếu từng file với `manifest-sha256.csv`. Truyền ZIP cùng reviewed
commit/nhánh cho người nhận qua kênh nhóm; **TestResults được Git ignore, push code không
tự gửi ZIP/ảnh**. Gói không chứa toàn bộ repo, cần nhận code qua Git riêng. Nếu đổi source
nghiệp vụ sau evidence thì phải chạy test phù hợp và xuất gói mới, không tái sử dụng nhãn Pass cũ.

## 4. Tài khoản, dữ liệu và demo

Làm theo [hướng dẫn QA6](../handover/qa6-run-demo.md): prerequisites, launcher fresh
không phá dữ liệu cũ, năm tài khoản với password tự nhập kín, dữ liệu seed nền,
chuẩn bị5MCQ có passage/audio, Practice và PracticeExam weighted, kịch bản7bước.

Không cần sửa SQL để thực hiện demo. QA5 runner cũng tái tạo đủ hành trình bằng UI,
nhưng tự dọn sau test; khác với launcher QA6 giữ volume cho rehearsal thủ công.
Đăng nhập sai sau đổi environment không được reset DB: seeder không reset password cũ.

## 5. Giới hạn phải nói khi báo cáo

1. Audio URL ngoài của đề cũ không bảo đảm bytes bất biến; đề mới dùng upload nội bộ.
2. Final đã có Attempt không được thay/gỡ, Clone không vượt khóa, chưa có sửa điểm hồi tố.
3. Backup SQL và kho audio cùng thời điểm; volume/audio hash không thay thế backup.
   Đã viết quy trình và kiểm tra sau restore; **chưa diễn tập restore backup thực tế** trong QA6.
4. Teacher Attempt report/lazy finalize đường Teacher chưa được bổ sung trong nhóm QA này.
5. Paid checkout, offline retry, timeout thật trên browser và Final/Clone rehearsal mở rộng
   còn cần biên bản riêng nếu nhóm muốn nghiệm thu thủ công cả13dòng A1; không ngăn dùng
   bộ demo Practice–Assessment–Result đã xác minh, nhưng không được trình bày là đã test UI hết.

## 6. Checklist bàn giao

- [x] Có testcase logic/expected/actual và CSV outcome thật từ TRX.
- [x] Có lỗi, retest, log test máy đọc được và48ảnh UAT.
- [x] Có launcher tái tạo tài khoản/dữ liệu nền và hướng dẫn dựng nội dung demo không sửa SQL.
- [x] Đã kiểm fresh build/migration/seed/5actor login trên môi trường riêng.
- [x] Đã kiểm launcher từ chối project có sẵn và port bận, không reset/dừng dịch vụ cũ.
- [x] Có hướng dẫn chạy/config/migration và kịch bản demo có thứ tự.
- [x] Ghi rõ audio ngoài, Final đã dùng, backup SQL/audio và giới hạn bằng chứng.
- [ ] Nhóm nhận ZIP và đối chiếu hash, chạy rehearsal, ghi người/ngày xác nhận.
- [ ] Commit/push/PR, CI và review cuối — chưa được yêu cầu trong QA6.

**QA6 hoàn thành phần chuẩn bị bàn giao local.** Hai mục cuối là hoạt động phối hợp/
phát hành tiếp theo, không tự ký thay nhóm hoặc tự push.
