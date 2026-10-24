# Task 03 — Thống kê doanh thu và thanh toán

Phụ trách: Dương (`NDuong05`). Nhánh: `feature/duong-admin-revenue`, phát triển từ Task 02 tại commit `a46e9ab`. Trạng thái review/merge của PR #66 chưa được xác nhận trong lượt triển khai này. Chưa tự commit hoặc push phần Task 03.

## Chức năng

- Admin mở `/Admin/Revenue` để xem doanh thu VND, số đơn và trạng thái thanh toán.
- Lọc từ ngày đến hết ngày theo giờ Việt Nam; cho phép chỉ nhập một đầu ngày hoặc để trống để xem toàn bộ lịch sử.
- Chỉ hỗ trợ VND theo `CheckoutOrderService` và `PaymentConfirmationService`. Không có ô chọn tiền tệ. Tham số tương thích `currency` chỉ nhận VND hoặc để trống; mã khác bị từ chối.
- Chọn thống kê theo ngày hoặc tháng. Dữ liệu chuỗi thời gian được trả trong DTO để Dashboard có thể dùng cho biểu đồ.
- Biểu đồ hiển thị tối đa 12 mốc có doanh thu VND gần nhất. Các ngày/tháng không có doanh thu không được tự chèn thành điểm 0.
- Bảng số liệu theo kỳ có 20 dòng/trang, giữ khoảng ngày, tiền tệ và cách nhóm khi chuyển trang.
- Không thêm migration và không thay đổi cơ chế xác nhận thanh toán, callback hoặc cấp quyền học.

## Quy tắc ghi nhận doanh thu

Một Order chỉ được ghi nhận doanh thu khi có ít nhất một Payment thỏa mãn đồng thời:

1. `Status == PaymentStatus.Succeeded`.
2. `Amount > 0` và bằng `Order.Amount`.
3. `PaidAtUtc` khác null.

Nếu cùng Order có nhiều Payment hợp lệ, lấy thời điểm `PaidAtUtc` sớm nhất. Doanh thu sử dụng `Order.Amount` một lần, không cộng từng lần thử thanh toán.

**Thứ tự thực hiện là khử trùng theo Order trước, rồi mới lọc thời gian.** Ví dụ một Order có receipt hợp lệ vào ngày 23/10 và bản ghi trùng vào ngày 24/10: doanh thu chỉ thuộc ngày 23/10. Báo cáo riêng ngày 24/10 không được tính lại Order đó.

Pending, Failed và Cancelled không tạo doanh thu. Các Payment thành công nhưng sai số tiền, số tiền bằng 0 hoặc thiếu thời điểm thanh toán cũng không tạo doanh thu. Service không tự dùng ngày tạo Payment thay thế khi thiếu `PaidAtUtc`; dữ liệu lịch sử không đủ thông tin phải được đối chiếu thay vì đoán ngày ghi nhận.

Chỉ số `UnrecognizedSucceededOrders` cảnh báo số Order thuộc nhóm tạo đơn đã chọn, có trạng thái thành công nhưng không có receipt hợp lệ như trên. Cảnh báo này không tự sửa dữ liệu và không thay đổi kết quả xác nhận thanh toán trước đó.

## Hai phạm vi thời gian khác nhau

| Nhóm chỉ số | Thời gian được lọc |
|---|---|
| Doanh thu và `PaidOrders` | Thời điểm receipt thành công hợp lệ đầu tiên của Order |
| Tổng Order và các trạng thái | `Order.CreatedAtUtc` |
| `UnrecognizedSucceededOrders` | Nhóm Order theo ngày tạo, thiếu receipt hợp lệ trong toàn bộ lịch sử |

Vì vậy `PaidOrders` không bắt buộc bằng `SucceededOrders` trong một khoảng ngày. Một đơn tạo ngày 22/10 nhưng trả tiền ngày 23/10 được ghi vào doanh thu ngày 23/10, còn số đơn mới của ngày 23/10 không bao gồm đơn đó. Giao diện ghi rõ hai phạm vi để tránh hiểu nhầm.

Quy tắc trạng thái cho mỗi Order giống Checkout: có Succeeded thì thành công; nếu chưa thành công mà có Failed thì thất bại; tiếp theo là Cancelled; còn lại là Pending, gồm trường hợp chưa có Payment. Một Order chỉ thuộc một nhóm, nên tổng bốn trạng thái bằng tổng số Order. Không tự xem một Order hết hạn là Failed.

Trang Admin Transactions hiện dùng trạng thái Payment mới nhất. Task 03 không thay đổi cách xử lý của trang đó; dữ liệu lịch sử nhiều trạng thái có thể cho kết quả khác với quy tắc ưu tiên của Checkout và báo cáo này.

## Múi giờ và tiền tệ

`AdminDateRange` chuyển đầu ngày UTC+7 về UTC, dùng điều kiện `>= StartUtc` và `< EndExclusiveUtc`. Ví dụ ngày 23/10/2026 tương ứng từ 22/10 lúc 17:00 UTC đến trước 23/10 lúc 17:00 UTC. Điều kiện này lấy cả phần giây nhỏ cuối ngày mà không phải tự đặt 23:59:59.

Ngày và tháng được nhóm bằng `EF.Functions.AtTimeZone(PaidAt, "SE Asia Standard Time")`, sau đó lấy năm/tháng/ngày. SQL Server chuyển cùng một thời điểm sang giờ Việt Nam đúng cả khi bản ghi lưu offset `+00:00` hoặc `+07:00`.

Canonical receipt dùng `MIN` trực tiếp trên `DateTimeOffset`. Bản thử nghiệm lấy `UtcDateTime` bên trong phép `MIN` không được EF Core dịch thành SQL trong môi trường này, nên đã thay bằng cách SQL Server hỗ trợ và kiểm tra ranh giới offset bằng test.

Truy vấn Order cố định điều kiện `Currency == "VND"` ngay tại SQL. Các bản ghi ngoại tệ cũ không xuất hiện trong tổng tiền, số đơn hoặc biểu đồ. Không đổi ngoại tệ thành VND và không quy đổi tỷ giá. Payment không có trường Currency riêng.

## Luồng code và truy vấn

`AdminController.Revenue` → `IAdminRevenueQueryService` → `AdminRevenueQueryService` → SQL Server → `AdminRevenueReport` → `AdminRevenueViewModel` → Razor View.

- Controller xử lý HTTP, validation đầu vào và trả View; không tự truy cập DbContext hoặc tính doanh thu.
- DI đăng ký `IAdminRevenueQueryService` bằng `AddScoped`, cung cấp implementation và DbContext theo request.
- Service kiểm tra `HasActiveRoleAsync` trước khi đọc báo cáo. Người không có Admin đang hoạt động nhận kết quả null; Controller trả Forbidden khi phù hợp.
- Các truy vấn đọc dùng `AsNoTracking`. EF không cần giữ entity để cập nhật vì báo cáo không ghi dữ liệu.
- LINQ `Where`, `GroupBy`, `Min`, `Sum`, `Count` và join được EF Core dịch thành truy vấn SQL. SQL trả các dòng đã tổng hợp; không tải toàn bộ Order/Payment rồi cộng trong C#.
- Payment được nhóm theo Order để lấy receipt đầu tiên. Các cờ trạng thái cũng được tổng hợp theo Order, rồi left join với Order để giữ các đơn chưa có Payment.
- Chỉ ghép các nhóm tiền tệ đã tổng hợp trong C# để tạo DTO. Các câu truy vấn dùng chung một DbContext được await tuần tự.
- Phân trang trong ViewModel chỉ áp dụng lên các dòng ngày/tháng đã tổng hợp tại SQL, không phân trang hoặc lọc dữ liệu Order/Payment thô trên bộ nhớ.

## File liên quan

| Đường dẫn | Vai trò |
|---|---|
| `src/EnglishLearningPlatform.Application/Administration/IAdminRevenueQueryService.cs` | Interface, report DTO, summary DTO và period DTO |
| `src/EnglishLearningPlatform.Infrastructure/Administration/AdminRevenueQueryService.cs` | Phân quyền service và truy vấn tổng hợp EF Core |
| `src/EnglishLearningPlatform.Infrastructure/DependencyInjection.cs` | Đăng ký service scoped |
| `src/EnglishLearningPlatform.Web/Controllers/AdminController.cs` | Endpoint GET Revenue và validation HTTP |
| `src/EnglishLearningPlatform.Web/Models/Admin/AdminRevenueViewModel.cs` | Chọn chuỗi ngày/tháng và phân trang dòng tổng hợp |
| `src/EnglishLearningPlatform.Web/Views/Admin/Revenue.cshtml` | Form lọc, số liệu, cảnh báo và biểu đồ |
| `src/EnglishLearningPlatform.Web/Views/Admin/Index.cshtml` | Đường dẫn từ Dashboard đến báo cáo |
| `src/EnglishLearningPlatform.Web/Views/Shared/_LayoutAdmin.cshtml` | Điều hướng Admin tới báo cáo |
| `src/EnglishLearningPlatform.Web/wwwroot/css/admin.css` | Bố cục báo cáo và responsive |
| `tests/EnglishLearningPlatform.IntegrationTests/AdminRevenueQueryTests.cs` | Dữ liệu mẫu, ranh giới ngày/tháng, validation và authorization |
| `.github/workflows/ci.yml` | Gán ba lớp test mới vào integration shard 3 |

Các test endpoint/DTO bảo mật hiện có được cập nhật khi bổ sung endpoint Admin mới. Danh sách file cuối cùng cần đối chiếu bằng `git diff --name-only` trước khi stage.

## Kiểm thử và dữ liệu chứng minh

Sau khi giới hạn VND, đã chạy lại **140/140 integration test** liên quan trên database test riêng. Bằng chứng mới: `TestResults/task03-vnd/vnd-regression.trx`. Test có bản ghi ngoại tệ để khẳng định nó bị loại khỏi mọi chỉ số, và kiểm tra URL/service từ chối yêu cầu USD/JPY. UAT VND mới đạt **17/17 kiểm tra** tại `TestResults/admin-revenue-2026-10-24T12-03-38-495Z/`; kiểm tra live riêng đối chiếu số tiền và 6 trạng thái đơn đạt, có ảnh 320/390/768/1440px trong `TestResults/task03-vnd/`. Các kết quả trình duyệt bên dưới là lịch sử trước sửa.

8 ca mới thuộc ba lớp: `AdminRevenueQueryTests`, `AdminRevenueBoundaryTests`, `AdminRevenueAuthorizationTests`. Lượt chạy riêng ban đầu đạt trên SQL Server Testcontainers. Lượt hồi quy cuối đạt **140/140 integration test liên quan**, gồm cả Dashboard, quyền Admin, CheckoutFlow và CommercePayment; dùng `tests/low-memory.runsettings`, SQL Server local và database ngẫu nhiên riêng của từng fixture qua `ELP_TEST_SQLSERVER` (không dùng database ứng dụng). Toàn bộ unit test đạt **542/542**.

Một lượt chạy rộng song song với Docker build đã phải dừng do Docker quá tải; Docker được khởi động lại và các môi trường web local được khôi phục. Kết quả hồi quy được ghi nhận ở lượt chạy tuần tự sau đó, không tính lượt đã dừng là đạt.

Bằng chứng local nằm trong `TestResults/task03/`: `task03-regression.trx`, `task03-unit.trx`, `security-audit.csv`, và HTML mẫu `sample-revenue-day.html` / `sample-revenue-month.html`. Kiểm tra phân nhóm CI đạt **70 lớp, mỗi lớp nằm đúng một shard**; security audit không phát hiện credential. Đây là kết quả local, chưa thay cho CI của PR mới.

## Kiểm tra trình duyệt

`tests/browser/admin-revenue-uat.mjs` nhận `ELP_UAT_URL` (mặc định `http://localhost:8080`), `ELP_ADMIN_EMAIL`, `ELP_ADMIN_PASSWORD` qua environment. Không đưa mật khẩu vào source hay lệnh đã lưu. Với `ELP_REVENUE_SAMPLE_DIR=TestResults/task03`, script kiểm tra thêm HTML mẫu đã render từ integration test, không tạo giao dịch trên web đang chạy.

- Revenue UAT: **17 kiểm tra đạt**, ảnh ở 320/390/768/1440px, gồm trang live, form lọc/reset/validation và báo cáo mẫu có biểu đồ. Kết quả: `TestResults/admin-revenue-2026-10-24T11-08-20-240Z/`.
- Dashboard UAT sau khi thêm mục Doanh thu: **19 kiểm tra đạt**. Kết quả: `TestResults/admin-dashboard-2026-10-24T11-08-37-846Z/`.

HTML/ảnh có nhãn nguồn fixture trong báo cáo UAT; số tiền mẫu không phải doanh thu live. Không commit toàn bộ TestResults. Chọn ảnh cần thiết khi đính kèm PR.

Lệnh kiểm tra code:

```powershell
dotnet test tests/EnglishLearningPlatform.IntegrationTests --settings tests/low-memory.runsettings --filter "FullyQualifiedName~AdminRevenue|FullyQualifiedName~AdminDashboard|FullyQualifiedName~AdminOverview|FullyQualifiedName~AdminReadOnlyTests|FullyQualifiedName~AdminManagementTests|FullyQualifiedName~AdversarialRoleLayoutStressTests|FullyQualifiedName~SecurityConfigurationTests|FullyQualifiedName~CheckoutFlowTests|FullyQualifiedName~CommercePaymentTests"
dotnet test tests/EnglishLearningPlatform.UnitTests
python scripts/ci/check-test-shards.py
```

Nếu muốn xuất HTML mẫu từ test, đặt `ELP_REVENUE_EVIDENCE_DIR` thành đường dẫn thư mục kết quả local trước khi chạy; đây là lựa chọn tạo bằng chứng, không ảnh hưởng kết quả truy vấn.

Fixture báo cáo chính chứa các lần thử Failed, Cancelled, nhiều receipt thành công của cùng Order, sai số tiền, thiếu ngày, số tiền 0 và một bản ghi ngoại tệ để kiểm tra nó bị loại khỏi báo cáo VND. Các tình huống bất thường này chỉ nằm trong database test:

| Phạm vi | Doanh thu VND | Order ghi nhận VND |
|---|---:|---:|
| Toàn bộ lịch sử fixture | 560 | 4 |
| Ngày 23/10/2026 | 420 | 3 |

VND toàn lịch sử có 10 Order: 7 thành công, 1 Pending, 1 Failed, 1 Cancelled; 3 Order thành công không đủ receipt hợp lệ nên không tạo doanh thu. Bản ghi ngoại tệ không được trả thành dòng riêng và không ảnh hưởng các chỉ số.

Test ranh giới kiểm tra đầu/cuối ngày, đầu/cuối tháng và offset `+07:00`; số tiền 20 và 30 thuộc tháng 10, còn 10 thuộc tháng 9 và 40 thuộc tháng 11. Có kiểm tra database nghiệp vụ rỗng, Guest phải đăng nhập, Student/Teacher bị từ chối, Admin khóa/vô hiệu hóa bị từ chối, khoảng ngày và mã tiền sai bị chặn.

## Hướng dẫn demo và giải thích

1. Đăng nhập Admin, mở Báo cáo doanh thu từ Dashboard hoặc menu.
2. Chọn khoảng ngày và cách nhóm ngày/tháng, bấm Xem báo cáo. Đơn vị cố định là VND.
3. Giải thích vì sao doanh thu và số đơn mới có thể khác phạm vi; chỉ đúng hai nhãn thời gian trên giao diện.
4. Chỉ ra một Order thử thanh toán nhiều lần nhưng doanh thu chỉ được cộng một lần.
5. Cho xem báo cáo VND và trạng thái rỗng khi không có dữ liệu phù hợp.

Dữ liệu trong integration test chỉ chứng minh tính đúng; không chèn fixture test vào database ứng dụng. Bộ demo local cũ `DEMO-TASK03-20261024` đã được sao lưu rồi thay theo yêu cầu người dùng: dùng hai khóa có phí và hai học viên sẵn có, giá đọc trực tiếp từ Course, chỉ VND, thời gian sau khi khóa/học viên tồn tại. Failed/Cancelled có Order riêng; Pending không có Payment; Succeeded có Payment khớp giá và Enrollment duy nhất. Không thay đổi giá khóa học, tài khoản hoặc khóa có bài học. Đây là dữ liệu demo local, không phải giao dịch ngân hàng thật. File backup/manifest nằm trong `TestResults/course-demo/`, không đưa lên Git.

Giới hạn: báo cáo không phải snapshot nguyên tử giữa nhiều câu truy vấn khi dữ liệu đang đổi; không hoàn tiền/đổi tiền; biểu đồ không lấp ngày trống; các dòng tổng hợp toàn khoảng được lấy về để phục vụ bảng và biểu đồ. Tối ưu index hoặc giới hạn phạm vi theo tải thực tế thuộc công việc đo đạc tiếp theo. Hoàn tất local chưa thay thế CI, review và merge của leader.
