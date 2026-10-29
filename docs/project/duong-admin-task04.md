# Task 04 – Integration test cho Admin

## Phạm vi

Kiểm tra Task 01–03 bằng xUnit, EF Core và SQL Server thật qua
`IntegrationTestFactory` / `DatabaseFixture` hiện có. Không thay đổi nghiệp vụ,
schema, dữ liệu demo hoặc cơ chế xác nhận thanh toán.

Mặc định fixture khởi động SQL Server Testcontainers. Mỗi lớp có database riêng;
không dùng database ứng dụng. `tests/low-memory.runsettings` chạy tuần tự để hạn
chế số container SQL Server hoạt động đồng thời.

Fixture cũng hỗ trợ `ELP_TEST_SQLSERVER`, giống chế độ SQL Server dùng chung
trong CI. Khi dùng chế độ này, từng fixture vẫn tạo và xóa database riêng có
tên ngẫu nhiên; không sử dụng database `EnglishLearningDb` của ứng dụng.

## Đối chiếu yêu cầu

| Yêu cầu | Test bao phủ |
|---|---|
| Admin được truy cập, Student/Teacher bị từ chối | `AdminReadOnlyTests`, `AdminDashboardAuthorizationTests`, `AdminOverviewAuthorizationTests`, `AdminRevenueAuthorizationTests`: kiểm tra service và MVC |
| Dashboard đếm đúng dữ liệu nhiều trạng thái | `AdminDashboardQueryTests.Dashboard_CountsEmptyThenMixedData_InSql_WithoutCountingPaymentRetriesTwice` |
| Database nghiệp vụ rỗng không lỗi | Dashboard test ở trên; `AdminOverviewEmptyTests`; `AdminRevenueAuthorizationTests`; `AdminRevenueAcceptanceTests` kiểm tra MVC trước khi thêm dữ liệu |
| Lọc giao dịch theo ngày và trạng thái | `AdminDateFilterTests.AllLists_FilterVietnamCalendarDay_BeforeCountingAndPaging` và theory kết hợp mới |
| Chỉ ghi nhận doanh thu thành công hợp lệ bằng VND | `AdminRevenueQueryTests.Revenue_UsesFirstValidReceiptPerOrder_ExcludesNonVnd_AndKeepsCreationCohortDistinct` |
| Một Order nhiều Payment không tăng doanh thu | Test trên và `AdminRevenueAcceptanceTests.YearBoundary_RetriesCannotMoveBookedRevenue_AndMonthlyTotalsReconcileWithDailyTotals` |
| Phân trang giữ bộ lọc, không thiếu/trùng kết quả | Theory mới trong `AdminDateFilterTests`; các test kết hợp trong `AdminOverviewDetailTests`; test phân trang báo cáo trong `AdminRevenueQueryTests` |
| Ranh giới ngày Việt Nam, gồm hết ngày kết thúc | `AdminDateFilterTests`, `AdminOverviewDetailTests`, `AdminRevenueBoundaryTests` và test ranh giới năm mới |
| Khoảng ngày sai và đầu ngày mở | `AdminDateFilterTests`, `AdminOverviewDetailTests`, `AdminRevenueBoundaryTests`, `AdminRevenueAuthorizationTests` |

## Test bổ sung

### Giao dịch: ngày + tìm kiếm + trạng thái + phân trang

Theory chạy cho Pending, Succeeded, Failed và Cancelled. Mỗi trường hợp có 22
đơn khớp, chia thành hai trang (20 + 2), cùng các đơn nhiễu: ngoài khoảng ngày,
khác trạng thái hoặc khác tên khóa học.

- Kiểm tra chính xác tập OrderId giữa hai trang, không trùng/thiếu.
- Các đơn cùng thời điểm kiểm tra thứ tự phân trang ổn định.
- Pending bao gồm đơn chưa có Payment và đơn có Payment Pending.
- Giao dịch hiển thị lần thử mới nhất theo quy tắc hiện có.
- Đi theo liên kết trang 2 thực tế từ HTML, xác nhận giữ đủ bốn bộ lọc.
- Kiểm tra đọc dữ liệu không làm tăng ChangeTracker hoặc thay đổi số Order/Payment.

### Doanh thu qua ranh giới năm

Hai đơn VND thành công có giá 199.000 và 299.000 đồng. Cả hai được tạo ngày
31/12/2026 theo giờ Việt Nam, nhưng thanh toán lần đầu hợp lệ ở hai phía của
00:00 ngày 01/01/2027. Có thêm Failed, Cancelled, Pending và các lần thử trùng.

- Ngày 31/12 ghi nhận 199.000 đồng, ngày 01/01 ghi nhận 299.000 đồng.
- Bản ghi thanh toán thành công lặp lại sau đó không chuyển ngày ghi nhận hoặc
  làm tăng tổng doanh thu.
- Tổng doanh thu 498.000 đồng và 2 đơn được đối chiếu với tổng theo ngày/tháng.
- Số đơn theo ngày tạo tách biệt với doanh thu theo ngày thanh toán.
- Số liệu năm khác nhau không bị gộp chỉ vì cùng số tháng.

## Lệnh kiểm tra

```powershell
dotnet test tests/EnglishLearningPlatform.IntegrationTests/EnglishLearningPlatform.IntegrationTests.csproj --configuration Release --settings tests/low-memory.runsettings --filter "FullyQualifiedName~AdminDateFilterTests|FullyQualifiedName~AdminRevenueAcceptanceTests" --logger "trx;LogFileName=task04-new.trx" --results-directory TestResults/task04

dotnet test tests/EnglishLearningPlatform.IntegrationTests/EnglishLearningPlatform.IntegrationTests.csproj --configuration Release --no-build --settings tests/low-memory.runsettings --logger "trx;LogFileName=task04-regression.trx" --results-directory TestResults/task04

dotnet test tests/EnglishLearningPlatform.UnitTests/EnglishLearningPlatform.UnitTests.csproj --configuration Release --logger "trx;LogFileName=task04-unit.trx" --results-directory TestResults/task04

python scripts/ci/check-test-shards.py
```

`AdminRevenueAcceptanceTests` được gán vào shard 3. Không bỏ qua test cũ hoặc
nới assertion để đạt kết quả. TRX trong `TestResults` là bằng chứng local, không
commit vào Git.

## Điều chỉnh request của hai test form giảng viên

Lượt regression đầu tiên phát hiện hai test POST form điểm thập phân thiếu
trường ẩn `__Invariant=Input.PassingScore` do MVC number-input tag helper sinh
ra. Request thủ công vì vậy không giống request trình duyệt và có thể bị
validation điểm 0–100 trước khi tới nghiệp vụ cần kiểm tra.

Đã thêm trường ẩn vào hai request trong `TeacherAssessmentConfigurationTests`
và `TeacherAssessmentDraftTests`. Giữ nguyên các assertion kiểm tra lỗi
TargetSkill, phân quyền, chống sửa Owner/Status/Course, trạng thái HTTP và số
điểm lưu trong SQL. Không sửa controller, service, validator hoặc form thật.

## Kết quả local

- Nhóm lọc ngày và doanh thu mới: **9/9 pass**, gồm 5 ca bổ sung, chạy bằng
  SQL Server Testcontainers (`task04-new.trx`).
- Unit test: **542/542 pass** (`task04-unit.trx`).
- Regression lần đầu: **1.242 pass / 2 fail**, không skip
  (`task04-regression.trx`); hai lỗi request form được mô tả ở trên.
- Sau khi sửa request: cả hai lớp test giảng viên **39/39 pass**, chạy bằng
  SQL Server Testcontainers (`task04-teacher-forms.trx`).
- Kiểm tra shard: **71 lớp**, mỗi lớp đúng một shard.
- Regression cuối: **1.244/1.244 pass, 0 fail, 0 skip**, thời gian 4 phút 34 giây
  (`task04-regression-final.trx`). Lượt này dùng `ELP_TEST_SQLSERVER` với SQL Server
  Docker local và database riêng từng fixture, cùng cơ chế database của CI.

## Bàn giao

- Nhánh: `feature/duong-admin-integration-tests`, tách từ code PR #67.
- 6 file thay đổi: `AdminDateFilterTests.cs`, `AdminRevenueAcceptanceTests.cs`,
  `TeacherAssessmentConfigurationTests.cs`, `TeacherAssessmentDraftTests.cs`,
  `.github/workflows/ci.yml` và tài liệu này.
- Chưa commit/push. Khi tạo PR về `main`, cần PR #67 được merge trước để phần
  diff chỉ còn các thay đổi Task 04.
- Không đưa `compose.local-run.yml`, `.env` hoặc `TestResults` vào commit.
- Kết quả trên xác nhận local; CI và review/merge vẫn là bước bàn giao tiếp theo.
