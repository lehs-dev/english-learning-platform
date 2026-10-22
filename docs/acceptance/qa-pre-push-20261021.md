# Kiểm tra trước push QA2–QA6 — 21/10/2026

## Phiên bản

- Nhánh: `codex/stimulus-flow-completion`.
- Đã fetch và merge `origin/main` tại `e817991` (PR #56), không có conflict.
  Main đã squash các commit trước; tree của main và HEAD cũ giống nhau trước thay đổi QA.
- Commit sửa deadline/Enrollment/audio: `e37612d`.
- Commit Preview Practice, UI và test: `07a7ab14e87e27192affca6e96c3e04e6ea077a3`.
- Full backend suite chạy trên cùng nội dung source trước hai commit trên; chỉ thao tác
  Git, không sửa runtime/test source giữa chạy suite và commit. Browser live sau đó
  ghi đúng SHA `07a7ab1`; tài liệu QA còn dirty lúc chạy, không gọi đó là clean release.
- Fetch lại sau browser: main vẫn `e817991` và là ancestor của HEAD. Commit tiếp theo
  chỉ đóng gói tài liệu/demo QA6; không đổi source app đã kiểm tra.

## Kết quả thực tế

| Kiểm tra | Actual | Artifact local |
| --- | --- | --- |
| Build Release toàn solution | PASS, 0 warning / 0 error | Console `dotnet build EnglishLearningPlatform.sln --configuration Release`. |
| Toàn bộ unit | **390/390 pass**, 0 fail/skip | `TestResults/qa-push-20261021/unit.trx`. |
| Toàn bộ integration, chạy tuần tự | **1.143/1.143 pass**, 0 fail/skip, 11 phút 22 giây | `TestResults/qa-push-20261021/integration.trx`. |
| Browser fixture dùng JavaScript Assessment thật | **9/9 pass** | `TestResults/qa-push-20261021/browser-fixture.xml`. Không phải UAT toàn app. |
| Browser live từ Teacher tạo nội dung đến Student Result/Profile | **6/6 pass**, 0 JavaScript error, **48 ảnh** | `TestResults/qa5/pre-push-main-20261021/qa5-results.json` và PNG. |
| Security scanner self-test | **18/18 pass** | Console của `scripts/security-audit.py --self-test`. |
| Audit source/lịch sử Git trước commit tài liệu | **0 finding**, 64 commit reachable | `TestResults/qa-push-20261021/security-audit.csv/.txt`. Có lượt audit candidate đã commit riêng trước push. |
| Kiểm tra diff / cú pháp script | PASS | `git diff --check`, PowerShell parser, `node --check`. |

Integration dùng SQL Server2022 Testcontainers riêng; không dùng `ELP_TEST_SQLSERVER`
hoặc database của web8080. Browser live dựng app18085/SQL15055/audio tạm và tự dọn
sau chạy; không sửa dữ liệu nghiệp vụ bằng SQL. Web/database làm việc vẫn chạy.

Lượt browser chốt: **20:52:23–20:52:50 (+07:00)**, ngày21/10/2026.
Digest runtime/browser/runner vẫn là:
`3f43cb2d99bf4345196ec1a2a31a6b6ba0164ad8371cc33bd2515459003f5e21`.
Phạm vi hash theo [báo cáo QA5](qa5-journey-results.md), không phải hash toàn repo.

## Phạm vi PR và giới hạn

- Lazy-finalize chỉ Attempt quá hạn tại Student Take/History/Result, dùng giờ server.
- Save kiểm Enrollment hiện tại; fallback tab cũ không finalize sớm; audio Course-linked
  kiểm Enrollment/thanh toán ngay cả khi Student có lịch sử làm bài.
- Preview Practice chỉ owner Teacher, Draft/Published, không tạo/chỉnh kết quả.
- Sửa bảng History/Skill trên mobile; thêm test hồi quy và hành trình browser tái lập.
- Tài liệu A1/QA2–QA6, launcher demo Windows, script đóng gói evidence và kịch bản demo.
- **Không có migration mới**, không đổi công thức scoring/representative hiện có.

TestResults được Git ignore: push không tự đưa log/PNG/ZIP lên GitHub. Các artifact
cần được đính kèm riêng khi nhóm cần đối chiếu. Evidence QA6 trước đó vẫn giữ nguyên,
không đổi SHA/timestamp lịch sử để giả thành lượt mới.

Đây là kết quả local, **không phải CI GitHub xanh hoặc chữ ký rehearsal nhóm**.
Đường Teacher report Attempt/lazy finalize chưa bổ sung; audio URL ngoài cũ không
bảo đảm bất biến; Final đã có Attempt không được thay/gỡ; cần backup SQL cùng kho
audio. Xem [QA6](qa6-handover-results.md) và [hướng dẫn demo](../handover/qa6-run-demo.md).
