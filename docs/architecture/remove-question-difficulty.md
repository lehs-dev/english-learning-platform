# Loại bỏ độ khó khỏi câu hỏi

Ngày thay đổi: **24/10/2026**. Migration: `20261023165551_RemoveQuestionDifficulty`.

## Phạm vi

- Bỏ riêng `Difficulty` khỏi `Question`, các DTO, validation, tìm kiếm, ánh xạ và luồng tạo/sửa câu hỏi.
- `Up()` của migration chỉ có một thao tác: xóa cột `dbo.Questions.Difficulty`.
- `Down()` thêm lại cột nullable `nvarchar(32)`. Các migration cũ được giữ nguyên.
- Giữ nguyên các thuộc tính còn lại, ID, đáp án, trọng số, liên kết bài luyện tập/bài kiểm tra, dấu khóa và kết quả học viên. Công thức chấm điểm không thay đổi.
- Query string/form cũ có khóa `Difficulty` được MVC bỏ qua; đây không còn là bộ lọc hoặc trường cập nhật.

## Trước khi áp dụng lên database đang sử dụng

Development hiện bật `Database:ApplyMigrationsOnStartup`. Khởi động ứng dụng có thể tự áp dụng migration này. Trong thời gian chuẩn bị, đặt biến sau trong terminal chạy ứng dụng:

```powershell
$env:Database__ApplyMigrationsOnStartup = 'false'
```

Xác nhận connection string đang trỏ đúng database, tạo bản sao lưu và kiểm tra khả năng phục hồi. Nếu cần lưu riêng giá trị độ khó lịch sử, xuất kết quả truy vấn sau trước khi xóa cột:

```sql
SELECT Id AS QuestionId, Difficulty
FROM dbo.Questions
ORDER BY Id;
```

Mã mới có thể đọc/ghi database cũ khi cột nullable này còn tồn tại. Sau khi xóa cột, mã cũ còn truy cập `Difficulty` sẽ không tương thích; cần cập nhật các phiên ứng dụng cùng dùng database đó.

## Xem SQL và áp dụng có kiểm soát

Các lệnh dưới đây dùng connection string đã cấu hình cho dự án. Chỉ thực hiện bước cập nhật sau khi đã xác nhận database và sao lưu. Giữ tạo dữ liệu demo tắt nếu không có nhu cầu tạo dữ liệu mới.

```powershell
$env:Database__ApplyMigrationsOnStartup = 'false'
$env:LearningDemo__Enabled = 'false'

dotnet ef migrations script 20261015181741_AddQuestionAndStimulusContentLocks 20261023165551_RemoveQuestionDifficulty --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web

dotnet ef database update 20261023165551_RemoveQuestionDifficulty --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web
```

Không cần xóa database, tạo lại câu hỏi hoặc sửa migration ban đầu. Trên database đã ở migration ngay trước, SQL chỉ thay đổi cột độ khó và bảng lịch sử migration. Nếu database còn thiếu migration cũ, lệnh cập nhật sẽ chạy cả các migration đang thiếu; cần xem và đánh giá chúng riêng trước khi áp dụng.

## Kiểm tra và rollback

`QuestionDifficultyMigrationTests` kiểm tra database mới, nâng cấp dữ liệu cũ và rollback. Bài kiểm tra đối chiếu cấu trúc các cột còn lại cùng dữ liệu của mọi bảng nghiệp vụ trước/sau, gồm đáp án, điểm, bài làm, thời hạn và dấu khóa; bảng lịch sử EF được loại khỏi phép so sánh vì migration phải cập nhật bảng này. Migration cũng được kiểm tra chỉ có đúng một thao tác xóa cột.

```powershell
dotnet ef database update 20261015181741_AddQuestionAndStimulusContentLocks --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web
```

Rollback chỉ khôi phục cấu trúc cột; các giá trị `Difficulty` sau đó là `NULL`. Muốn lấy lại giá trị cũ, phục hồi từ bản sao lưu hoặc dữ liệu xuất riêng. Các dữ liệu còn lại không bị migration này chỉnh sửa.

Việc chuẩn bị mã nguồn và chạy kiểm thử không đồng nghĩa migration đã được áp dụng lên database làm việc của người dùng.

## Kết quả xác minh

- Solution build thành công, 0 warning, 0 error.
- Unit test: **386 đạt, 0 lỗi**. Chỉ bỏ các ca kiểm tra dành riêng cho thuộc tính độ khó đã xóa.
- Integration test: **1.155 đạt, 0 lỗi**, chạy bằng `tests/low-memory.runsettings` với cấu hình log chuẩn. Hai kiểm thử migration nêu trên nằm trong tổng này.
- Kiểm thử trình duyệt trên database tạm đã áp dụng migration: tạo, lưu nháp, sửa, validation, thêm/xóa đáp án và giao diện desktop/mobile đều đạt; không có lỗi JavaScript.
- Đối chiếu với bản mã ngay trước thay đổi: 16 tệp xử lý chỉ thay đổi phần `Difficulty`; model snapshot chỉ bỏ trường này. Không sửa migration lịch sử hoặc công thức chấm điểm.

Kết quả kiểm thử trên được ghi nhận ngày 24/10/2026. Bước cập nhật database cục bộ sau đó được ghi riêng bên dưới.

## Cập nhật database cục bộ ngày 24/10/2026

Đã cập nhật **`localhost/EnglishLearningDb`**, là database trong launch profile `LocalSqlServer`.

Database ban đầu ở migration `20261008035103_AddCheckoutOrderMetadata`, nên còn thiếu cả migration khóa nội dung. Sau khi được người dùng xác nhận cho phép áp dụng cả hai migration, đã chạy:

1. `20261015181741_AddQuestionAndStimulusContentLocks`: thêm `Questions.ContentLockedAtUtc` và `Stimuli.ContentLockedAtUtc`.
2. `20261023165551_RemoveQuestionDifficulty`: xóa `Questions.Difficulty`.

Đã tạo bản sao lưu `COPY_ONLY` với checksum, kiểm tra bằng `RESTORE VERIFYONLY`, phục hồi sang database tạm và chạy thử thành công trước khi cập nhật database gốc. Bản sao lưu được giữ tại:

```text
D:\sqlsever\MSSQL17.MSSQLSERVER\MSSQL\Backup\EnglishLearningDb_before_remove_difficulty_20261024_df6a8c6477f34165b8e86300c49897d4.bak
```

Kết quả kiểm tra sau cập nhật:

- Có đủ 8 migration, kết thúc ở `20261023165551_RemoveQuestionDifficulty`.
- `Questions.Difficulty` không còn tồn tại; hai cột khóa nội dung tồn tại và cho phép `NULL`.
- Cấu trúc các cột còn lại cùng dữ liệu của các bảng ứng dụng khớp dấu vân tay SHA-256 trước/sau. Bảng lịch sử EF và ba cột dự kiến thay đổi được loại khỏi phép so sánh.
- Ứng dụng dùng database thật trả HTTP 200 cho `/health`, `/Account/Login` và `/Courses`; đã tắt tạo dữ liệu demo trong lúc kiểm tra.
- Trước và sau cập nhật đều có 1 tài khoản, 3 role, 1 liên kết user-role; chưa có câu hỏi, bài làm hoặc khóa học trong database này.
- Database tạm và tiến trình ứng dụng dùng để kiểm tra đã được dọn; không cập nhật các database khác.
