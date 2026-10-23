# Kiểm tra và hoàn thiện luồng Student — 22/10/2026

Múi giờ báo cáo: Asia/Saigon (UTC+7). Base commit: `0ebd04b`;
thay đổi trong working tree chưa commit. Kết quả bên dưới là kiểm tra local,
không thay thế CI hoặc nghiệm thu của cả nhóm.

## Thay đổi

- Assessment autosave gửi `Answers.Index` cho câu có index gốc khác 0;
  Save/Submit chỉ gửi các câu còn thay đổi, kể cả index không liên tiếp.
  Đáp án đã lưu ở tab khác không bị gửi lại hoặc xóa bởi tab cũ.
- Thông báo lưu giữ lỗi của câu chưa lưu thành công khi câu khác lưu được;
  thử lại, hàng đợi, xác nhận nộp và hết giờ có regression riêng.
- Dashboard và My Courses dùng tiến độ hiện tại để chọn học tiếp, giữ nguyên
  ngày hoàn thành lịch sử khi Teacher thêm bài mới.
- Lịch giữ hoạt động của Lesson/Module đã ẩn nhưng bỏ liên kết không còn quyền đọc;
  liên kết xuất hiện lại khi nội dung được mở lại.
- Catalog không mời đăng ký khóa của Teacher Disabled. Quyền đọc của Student đã
  enroll vẫn được giữ, và count/paging/bộ lọc dùng cùng tập khóa khả dụng.
- Nút demo đăng nhập giữ đúng mật khẩu có ký tự đặc biệt; Production không xuất
  mật khẩu demo. Luồng đăng ký giữ đích ban đầu đến sau đăng nhập.
- Audio/video Lesson co theo khung nội dung; sửa tràn ngang đã phát hiện ở 320px.

Không thêm migration hoặc thay đổi chính sách thu tiền. Checkout, lịch sử thanh
toán và expiry của base commit được kiểm tra hồi quy cùng các luồng trên.

## Cách chia test và kết quả

Không chạy `dotnet test` toàn solution. Build dùng một MSBuild worker,
`/nodeReuse:false`, `UseSharedCompilation=false`, `BuildInParallel=false`.
Test dùng `tests/low-memory.runsettings`: một worker, tắt song song assembly và
test collection. Tổng cộng 28 lớp được chọn, mỗi lớp một process tuần tự;
hai case Practice context mới chạy riêng sau khi thêm vào test.

| Kiểm tra | Kết quả |
| --- | --- |
| Build Unit/Integration và helper UAT | Đạt, 0 warning/error |
| Unit theo các nhóm Student | 102/102 đạt |
| Integration theo các nhóm Student, gồm 2 case context bổ sung | 341/341 đạt |
| Chromium fixture autosave, chạy tuần tự | 11/11 đạt |
| UAT ứng dụng thật, 6 nhóm riêng | 19/19 bước đạt |
| JavaScript error trong 6 nhóm UAT cuối | 0 |
| Responsive | 320px và 390px: không tràn document; sidebar đóng bằng Escape và trả focus |
| `git diff --check`, cú pháp Bash/JavaScript | Đạt |

Các nhóm .NET bao phủ đăng ký/đăng nhập/đăng xuất/Profile, catalog và ghi danh miễn
phí, quyền đọc Course/Module/Lesson/resource, thay đổi progress, Dashboard/gợi ý,
Practice/làm lại/feedback lịch sử, đơn hàng/webhook/replay/expiry/thanh toán hợp lệ,
Placement/Assessment/attempt/resume/hết giờ/submit/điểm/history và hoàn thành khóa.

Test mới chữa bài có hai loại Passage/Audio: trả ngữ cảnh trước/sau khi rút bài,
encode HTML, đọc audio nội bộ 200/Range 206 và từ chối Student khác đã enroll.
Test sparse-index dùng đúng tên Razor, kiểm tra đáp án, ID và thời gian lưu của
tab khác không bị thay đổi, và chấm đúng 60% khi trả lời đúng 3/5 câu.

## Browser trên ứng dụng thật

Một app riêng tại `http://127.0.0.1:18122`, database
`ELP_StudentUAT_13096c7ed3f446dab09f226a98822bdc`, seed bằng cơ chế Development có
sẵn. Không dùng database ứng dụng. Mật khẩu seed sinh riêng trong environment,
có `&` và dấu nháy để kiểm tra nút demo, không ghi vào report hoặc ảnh.

Mỗi nhóm chạy bằng process Node riêng với một Chromium/context; nhóm Assessment
tạm mở hai tab trong cùng context. Context và browser đóng sau từng nhóm.

| Nhóm | Bằng chứng UI thực tế | Bước đạt |
| --- | --- | ---: |
| auth | Chọn khóa → Login → Register → Login → đúng khóa; nút demo giữ ký tự mật khẩu và đăng nhập được | 2 |
| learning | Ghi danh → Completed/Incomplete/reload → CTA Final → làm Final 100% → khóa hoàn thành và giữ ngày hoàn thành | 4 |
| practice | Hub theo enrollment → passage → nộp/chữa bài → làm lại/kết quả mới nhất → học tiếp; Student khác không đọc kết quả | 3 |
| assessment | Placement 15 câu có audio nội bộ; autosave câu 2/câu cuối, hai tab/reload, nộp 100%, History và quyền riêng tư | 4 |
| checkout | Pending không cấp quyền; Failed/Cancelled thử lại bằng đơn mới; sandbox Succeeded mở Lesson và giữ ba trạng thái lịch sử | 3 |
| responsive | Dữ liệu qua UI; Dashboard/Hub/Course/Lesson/Result ở 390px và 320px, sidebar/Escape/focus | 3 |

Có 21 ảnh viewport của các lượt cuối; không chụp trang Account hoặc lưu storage
state/cookie/token. Đã xem ảnh Course completed, Practice feedback và Lesson mobile.
Mỗi report ghi hash source trước/sau, đều không đổi trong lượt tương ứng.

App UAT đã dừng; helper đã drop database GUID do chính nó tạo và xóa kho audio riêng.
Các fixture integration cũng tạo/drop database GUID riêng. Không reset database,
volume, tài khoản hoặc dữ liệu đang làm việc.

## Lượt lỗi và sửa lại

- Runner ban đầu chọn `CheckoutMergeTests` theo tên file. Đây là partial của
  `CheckoutFlowTests`, nên runner dừng đúng khi không tìm thấy lớp và không báo pass
  giả. Đã sửa danh sách, đối chiếu cả 28 lựa chọn với discovery và chạy tiếp các lớp
  chưa chạy. Không có test nghiệp vụ thất bại ở bước này.
- Browser Practice lần đầu có assertion phân biệt hoa/thường, trong khi CSS viết
  hoa nhãn đáp án. Đã sửa test theo text thực tế, giữ report cũ tại
  `browser/practice-attempt-1`, chạy lại riêng Practice đạt đủ 3 bước.
- Browser responsive phát hiện Lesson tràn 17px ở 320px do audio có kích thước
  mặc định. Đã sửa CSS media; giữ ảnh/report cũ tại `browser/responsive-attempt-1`;
  chạy lại riêng responsive đạt đủ 3 bước, gồm cả 320/390px.

## Chạy lại từng phần

Từ thư mục gốc trong Bash/Dev Container:

```bash
# Đặt ELP_TEST_SQLSERVER qua environment nếu dùng SQL Server có sẵn.
bash scripts/test-student-flow.sh --group auth
bash scripts/test-student-flow.sh --group catalog --no-build
bash scripts/test-student-flow.sh --group learning --no-build
bash scripts/test-student-flow.sh --group practice --no-build
bash scripts/test-student-flow.sh --group checkout --no-build
bash scripts/test-student-flow.sh --group assessment --no-build
bash scripts/test-student-flow.sh --group completion --no-build
node --test --test-concurrency=1 tests/browser/assessment-ui.test.mjs
```

Chỉ dùng `--no-build` khi binary đã được build từ source hiện tại. Không có `--group`
thì runner vẫn chạy từng lớp tuần tự, bỏ lớp trùng giữa các nhóm; `--list` không build
hoặc chạy test. Chạy UAT thật theo từng nhóm bằng `student-flow-uat.mjs` với app/DB
riêng như hướng dẫn trong [Browser README](../../tests/browser/README.md).

Artifact local bị Git ignore: `TestResults/student-flow-20261022/summary.json`,
TRX/log trong `dotnet*`, `practice-context`, `assessment-browser-fixtures.log`,
JSON và PNG trong `browser/<group>`. Không dùng SHA base làm SHA của bản source
chưa commit hoặc coi artifact trên máy này là đã có trên máy của thành viên khác.

Thanh toán browser dùng sandbox, không chứng minh provider thu tiền thật hoạt động.
Browser được kiểm tra là Chromium; không tuyên bố đã chạy Firefox/WebKit hoặc
chứng nhận accessibility toàn bộ website.
