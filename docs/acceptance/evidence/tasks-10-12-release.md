# Thực thi Công việc 10, 11, 12 — 11/10/2026

Branch: `feat/son-tasks-10-12-release-candidate`. Base: `a9e90e74fd56850c2977111dd2ab315cd07d1e4f`. Giờ hiển thị Asia/Saigon. Source: kế hoạch người dùng cung cấp, mã nguồn đang chạy và workbook tiến độ ID `1E_nodHM2VwFwqbon_2l6pzlnI02t1Z33` (Excel trên Drive, giữ nguyên định dạng).

## Phần triển khai

- Course hiển thị Final overview, thời lượng/số câu/lượt/điểm cao nhất và huy hiệu/ngày hoàn thành.
- Student có Take → POST Start → autosave/manual Save → Resume → Submit → Result/Skill Breakdown/history. GET không tạo lượt, dữ liệu lấy từ service/claims, không đưa answer key/giải thích ra HTML.
- Admin chỉ GET tra cứu users/transactions với search/status/pagination, không có quyền sửa nội dung teacher hoặc đọc đáp án.
- Global MVC CSRF; cookie HttpOnly/Lax/Secure ngoài Development; bỏ password runtime mặc định khỏi cấu hình, Compose và Dev Container. Hosted webhook vẫn xác minh HMAC raw bytes; local sandbox opt-in Development ký envelope riêng.
- Docker chờ SQL healthy, migration runner `--migrate` trước web, user `app`, image có revision label. `fresh-demo.sh` dùng project/volume riêng, từ chối project đã tồn tại. Không reset database dev.
- CI full history, audit trước build/tests, `main`/`feat/**`/PR/dispatch, artifact TRX/SHA/SDK/audit. Release gate kiểm tra working tree/SHA trước và sau.
- SP11/SP13/SP14/SP15, báo cáo ba môn và deck 28 slide có nguồn Markdown cùng DOCX/PDF/HTML/PPTX, font Unicode/OFL, exporter .NET và manifest. Xem [hồ sơ bàn giao](../../handover/README.md).

## Kiểm chứng trước commit

| Kiểm tra | Kết quả thực tế |
| --- | --- |
| Baseline | Release build, 84 unit + 247 integration Passed |
| Assessment query / UI | 35 query + 6 UI flow Passed; không cộng filter chồng vào tổng regression |
| Security / Admin | 17/17 Passed; [TRX focused](security/security-admin.trx) |
| Fresh Testcontainers seed | 4/4 Passed, không skip; [log](tasks-10-12/fresh-seeder-tests.log), [TRX](tasks-10-12/fresh-docker-seeder.trx) |
| Fresh Compose | 6 migrations áp dụng, migrate exit0, DB healthy/web chạy nonroot; [log migration](tasks-10-12/fresh-migrations.log), [status](tasks-10-12/fresh-compose-status.log) |
| Browser Chromium | 6/6 Passed, 19 PNG, desktop1440/mobile390, không JavaScript error; [report](tasks-10-12/browser-precommit/results.json) |
| Security scan | 14 scanner self-tests Passed, 0 suspicious patterns trên reachable history và working tree; xem [phạm vi audit](../security-audit.md) |
| Handover | Exporter build, XML OOXML, glyph coverage, SHA256 manifest Passed; visual review ghi riêng khi có |

Browser precommit report ghi base HEAD và `workingTreeClean=false`; SHA base không phải candidate đã commit. `sourceDigest`/image ID ghi snapshot thực được phục vụ. Lượt thử readiness ban đầu gặp connection-refused trước seed; suite đã thêm health polling và chạy lại thành công trên database demo mới. Những lỗi fixture/EF projection trong quá trình phát triển đã được sửa và focused suites chạy lại xanh.

UAT gồm Guest/catalog/protected login, Student2 paidHMAC→enroll→lesson/replay, Student1 lessons→80% Final→skill results→CompletedAt/resume, Teacher own+foreign ownership, Admin transaction/users+content denied và Student2 free enrollment/result privacy. Media ngoài bị chặn trong suite, chứng minh luồng demo Text chạy không phụ thuộc mạng ngoài; không chứng minh chất lượng audio/video ngoài.

## Chốt candidate và việc có điều kiện

Chỉ điền final release gate/remote CI bằng SHA/run/artifact thực tế. Candidate logs/TRX sau commit lưu riêng theo SHA; giữ record precommit để phân biệt provenance. Mỗi thay đổi sau commit/gate phải được kiểm tra theo phạm vi phù hợp.

Hai rehearsal người thật (15/10, 09:00–11:30 và 14:00–16:30 Asia/Saigon) chưa diễn ra trong phiên này. Các kịch bản/phân vai/biên bản đã chuẩn bị; browser automation không thay buổi họp có thành viên tham gia. RC tag theo kế hoạch chỉ chốt sau green CI và rehearsal. Không ghi tag/meeting/video hoặc hoàn thành task12 khi chưa có bằng chứng.

Workbook: `Sơn!F17/F18/F19` là 3 trạng thái; `Tiến độ chung!C23/C25` là công thức tuần5/6. Khi task10/11 hoàn thành nhưng task12 còn chờ, tổng trọng số là91.1%, tuần6 là `0.08/0.169≈47.34%`, không phải80%. Giữ công thức gốc thay vì ghi đè số0.8 như kế hoạch; task12 hoàn thành mới thành100%. Chỉ cập nhật trạng thái đã đủ bằng chứng.
