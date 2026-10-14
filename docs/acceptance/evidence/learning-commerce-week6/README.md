# Bằng chứng Tuần 6 Learning/Commerce

Đây là dữ liệu chạy thật, không phải số mục tiêu kế hoạch. [Biên bản/matrix](../../learning-commerce-week6.md) giải thích phạm vi và giới hạn; [chương báo cáo](../../../handover/learning-commerce-chapter.md) có hình minh họa chọn lọc.

- [run.json](run.json): candidate `ea1e4bb`, clean tree, SDK, assembly/image hash, các stage và cleanup.
- [database-evidence.json](database-evidence.json): query chỉ đọc sau UAT; Free/Paid đúng Enrollment/Payment/Order/progress và lịch sử.
- [ci-test-results.json](ci-test-results.json): 99 unit + 315 integration; từng case/outcome/duration, SHA và hash raw TRX từ artifact CI.
- [remote-ci-source.json](remote-ci-source.json): CI source `8c78d49` thành công và URL run. Test/runtime source không đổi tới candidate UAT; thay đổi sau đó ở reporter JSON.
- [source-manifest.json](source-manifest.json): SHA256 mỗi file src/tests/scripts/.github ở candidate UAT.
- [browser-testcases.csv](browser-testcases.csv): 10 testcase Learning/Commerce với expected/actual/actor/time/SHA/ảnh, UTF-8 dùng được trong Excel.
- [browser-learning-commerce/results.json](browser-learning-commerce/results.json): 10/10 Pass, source digest và 23 ảnh.
- [browser-existing/results.json](browser-existing/results.json): 6/6 Pass và 19 ảnh cho Guest/Student/Teacher/Admin, gồm Final/Completion hiện có.
- [build.log](build.log), [journeys.log](journeys.log), [fresh-migrations.log](fresh-migrations.log), [sql-version.txt](sql-version.txt): log và môi trường đã chạy.
- [security-audit.txt](security-audit.txt): phạm vi scan, 0 findings; [security-audit.csv](security-audit.csv) không có finding.
- `SHA256SUMS.txt`: manifest integrity của toàn bộ hồ sơ trong thư mục này, trừ chính manifest.

TRX gốc CI nằm trong artifact `test-results` của [GitHub Actions](https://github.com/lehs-dev/english-learning-platform/actions/runs/38529445056). Bản tải local ở `artifacts/learning-commerce-week6/ci-38529445056/test-results`. Raw log/TRX của baseline và các run thử lỗi được giữ riêng trong artifacts local; chúng không được đổi thành kết quả Pass.

UAT là Chromium automation, không có chữ ký nghiệm thu/rehearsal người thật. Ảnh chỉ chứa tài khoản demo; không có password, cookie, chữ ký HMAC hoặc connection string trong bộ hồ sơ.
