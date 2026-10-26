# Bộ dữ liệu demo mở rộng

Seeder này mở rộng dữ liệu local để có thể thử nhiều luồng trên giao diện. Nó chạy trong `Development` bằng lệnh `--seed-demo` hoặc cấu hình `LearningDemo:Enabled=true`. Seeder bổ sung theo tên cố định, giữ nguyên course/nội dung đã có, không xóa progress hoặc Attempt và không ghi đè dữ liệu đã được sửa.

## Nội dung được tạo

- 3 khóa học mới: **English Starter A1** và **English at Work A2** miễn phí; **Reading & Listening B1** trả phí.
- Mỗi khóa có 3 module và 3 lesson trong mỗi module. Mỗi lesson có nội dung đọc mẫu.
- Một bài luyện cho từng lesson và một checkpoint cho từng module. Khóa Everyday English ban đầu cũng nhận bài luyện ở lesson và module đang hiển thị; có thêm một Practice Draft để thử preview/soạn thảo.
- Question Bank có 20 câu Published và 4 câu Draft thuộc Grammar, Vocabulary, Reading và Listening; Reading dùng passage, Listening dùng audio hội thoại tiếng Anh được đóng gói cùng ứng dụng và lưu vào kho audio theo hash.
- Assessment độc lập: 2 Placement Test, 4 Skill Assessment và 1 Mock Exam. Có thêm một Assessment Draft và một Assessment Unpublished để thử màn hình quản lý Teacher.
- Mỗi khóa trong bốn khóa hiển thị ở Learning có Mixed Skills Assessment và Listening Skill Assessment. Các khóa miễn phí được ghi danh cho `student1`; khóa trả phí vẫn chưa ghi danh để thử checkout và chặn truy cập trước thanh toán.

Seeder không tạo Attempt hoặc lịch sử điểm giả. Student có thể tự làm bài từ UI và xem kết quả được ghi nhận thật.

## Bật seed trong lần chạy local

Trên máy Windows dùng SQL Server local và profile `LocalSqlServer`, chạy seed một lần:

```powershell
dotnet run --project src/EnglishLearningPlatform.Web --launch-profile LocalSqlServer -- --seed-demo
```

Lệnh này áp dụng migration còn thiếu, seed dữ liệu rồi thoát. Mật khẩu demo được lấy từ `LearningDemo:Password` trong .NET User Secrets hoặc environment hiện có. Sau khi lệnh hoàn tất, chạy web trong terminal của bạn:

```powershell
dotnet run --project src/EnglishLearningPlatform.Web --launch-profile LocalSqlServer
```

Mở `http://localhost:5098`. Mỗi cổng chỉ chạy một phiên web; dừng phiên cũ bằng **Ctrl+C** trước khi chạy lại. Seed một lần bằng `--seed-demo` không giữ cổng `5098`.

Giữ connection string và các cấu hình Development hiện có. Bật seed qua environment trong cùng terminal trước khi chạy ứng dụng:

```powershell
$env:LearningDemo__Enabled = 'true'
$demoCredential = Read-Host 'Mật khẩu demo local' -AsSecureString
$env:LearningDemo__Password = [System.Net.NetworkCredential]::new('', $demoCredential).Password
dotnet run --project src/EnglishLearningPlatform.Web
```

Nếu đã seed tài khoản trước đây, nhập lại đúng mật khẩu đã dùng lần đầu; Identity password không bị reset. Seed cần schema đã migration. Khi dùng Docker Compose, đặt `LearningDemo__Enabled=true` và nhập `LearningDemo__Password` trong environment local rồi khởi động `web`; volume `stimulus-audio-data` giữ audio giữa các lần dựng lại container. Không đưa mật khẩu vào repo hoặc chia sẻ terminal log có chứa environment.

Tài khoản dùng để khám phá:

| Email | Vai trò | Dữ liệu hữu ích |
| --- | --- | --- |
| `teacher1@demo.example.test` | Teacher sở hữu dữ liệu | Course, Question Bank, Practice, Assessment, passage và audio mẫu. |
| `teacher2@demo.example.test` | Teacher đối chứng | Dùng kiểm tra quyền sở hữu chéo. |
| `student1@demo.example.test` | Student đã enroll khóa miễn phí | Learning, Practice, Placement/Skill/Mock Assessment và kết quả. |
| `student2@demo.example.test` | Student chưa enroll | Dùng kiểm tra quyền truy cập; có thể thử checkout khóa trả phí. |
| `admin1@demo.example.test` | Admin | Tra cứu trang quản trị. |

Cả năm tài khoản dùng mật khẩu nhập ở lần seed đầu tiên. Guest là người xem khi đăng xuất, không phải tài khoản riêng.

### Luồng gợi ý

1. Đăng xuất để xem Home, Course catalog, filter và chi tiết khóa với vai trò Guest.
2. Đăng nhập Teacher1 để thử catalog khóa của mình, Question Bank, xem passage/audio, Practice và Assessment ở trạng thái Published/Draft/Unpublished.
3. Đăng nhập Student1 để mở khóa miễn phí, đánh dấu bài học, làm Practice, Placement Test, từng Skill Assessment và Mock Exam; nộp bài rồi xem điểm/kỹ năng.
4. Dùng Student2 hoặc Teacher2 để xác nhận nội dung bị chặn đúng quyền.
5. Dùng Student2 và [cấu hình local checkout sandbox](checkout-local.md) nếu muốn kiểm tra checkout; sau khi thanh toán thành công, Student2 mới học khóa trả phí.

Seed lại bằng cùng cấu hình để bổ sung các dòng còn thiếu. Những Practice/Assessment đã có cùng tên không bị dựng lại; Attempt, progress và mật khẩu được giữ nguyên. Muốn có môi trường thật sạch để chạy lại từ đầu, dùng một database demo mới theo [hướng dẫn QA6](../handover/qa6-run-demo.md), không xóa database đang làm việc.
