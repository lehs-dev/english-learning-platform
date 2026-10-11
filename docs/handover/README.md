# Bộ hồ sơ bàn giao Công việc 10–12

Nguồn được soạn theo mã nguồn candidate và kế hoạch bảo vệ 16/10/2026. Người phụ trách tổng hợp: Lê Hoàng Sơn — 3123560077. Ngày soạn: 11/10/2026, Asia/Saigon. Hồ sơ này không tự chứng nhận các AC đã Pass; SP14 và nhật ký evidence chứa kết quả thật của người chốt release.

| Tài liệu | Nguồn chỉnh sửa | Đầu ra |
| --- | --- | --- |
| SP11 · Hướng dẫn sử dụng | [SP11-user-manual.md](SP11-user-manual.md) | DOCX, PDF, HTML |
| SP13 · Kỹ thuật và vận hành | [SP13-technical-operations.md](SP13-technical-operations.md) | DOCX, PDF, HTML |
| SP14 · Kiểm thử và nghiệm thu | [SP14-test-acceptance.md](SP14-test-acceptance.md) | DOCX, PDF, HTML |
| SP15 · Yêu cầu và thiết kế | [SP15-requirements-design.md](SP15-requirements-design.md) | DOCX, PDF, HTML |
| Báo cáo đồ án ba môn | [report-three-courses.md](report-three-courses.md) | DOCX, PDF, HTML |
| Deck bảo vệ 28 slide | [defense-deck.md](defense-deck.md) | PPTX, PDF, notes DOCX |

Binaries nằm tại [exports](exports/). PDF nhúng font Unicode Noto Sans nên đọc/search/copy tiếng Việt được; font và giấy phép OFL ở assets. PPTX/DOCX dùng Noto Sans, cài font kèm theo hoặc dùng font Unicode thay thế trên máy bảo vệ. PPTX có speaker notes trong từng slide và tệp defense-speaker-notes.docx. Nguồn deck tách slide bằng dòng ---; mỗi slide có tiêu đề, 3–4 ý và ghi chú >.

Xuất lại sau khi chỉnh SP14/evidence:

```bash
bash scripts/package-handover.sh
```

Exporter C# chỉ cần .NET 10 SDK, không có NuGet dependency ngoài SDK và không tải/chạy mã từ xa. Nó kiểm tra 25–30 slides, glyph Unicode và XML trong tất cả DOCX/PPTX, tạo SHA256SUMS.txt. Xem HTML bằng browser và PDF/PPTX bằng viewer để kiểm tra trực quan trước bảo vệ. HTML là bản xem/print tiện lợi, không phải web deploy.

Đóng gói allowlist tài liệu nguồn/artifact, evidence ledger và contract:

```bash
ELP_HANDOVER_BUNDLE=/tmp/elp-handover.tar.gz bash scripts/package-handover.sh --bundle
(cd docs/handover && sha256sum -c SHA256SUMS.txt)
```

Kiểm tra manifest từ thư mục docs/handover (cd docs/handover rồi sha256sum -c SHA256SUMS.txt); các đường dẫn trong manifest tương đối tại đó. Bundle không tự thu thập toàn bộ log/TRX/screenshot trong thư mục tạm: người chốt release phải chuyển các tệp đã rà secret vào thư mục evidence và dẫn link từ SP14, rồi archive evidence riêng nếu cần. Không tạo tệp vắng mặt thành evidence giả.

Tại thời điểm soạn, rehearsal người thật ngày 15/10, remote CI, tag RC và spreadsheet chỉ chuyển trạng thái khi có bằng chứng tương ứng. Automation không thay biên bản có thành viên tham gia. Candidate SHA không nhúng vào mọi artifact để tránh circular commit SHA; ledger/TRX/manifest giữ provenance riêng.
