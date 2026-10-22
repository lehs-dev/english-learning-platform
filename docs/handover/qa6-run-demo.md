# QA6 — Chạy project và demo Practice–Assessment–Result

Ngày lập: 21/10/2026. Dành cho thành viên mới nhận code.
Đọc cùng [testcase và báo cáo QA6](../acceptance/qa6-handover-results.md).

## 1. Chuẩn bị

- Git, Docker Desktop có Engine Linux và Docker Compose v2; đủ RAM cho SQL Server.
- Windows: PowerShell 7 (`pwsh`). Chạy .NET trên máy cần .NET 10 SDK.
- Browser UAT cần Node.js, `npm ci` trong `tests/browser`, rồi `npx playwright install chromium` tại đó.
- Website là ASP.NET Core MVC/Razor: frontend và backend chạy chung web, không có lệnh chạy React riêng.
- Nhận đúng nhánh/commit đã review. `git status` để biết còn file chưa commit; không dùng `reset --hard` để cập nhật.
- Không gửi `.env`, user-secrets, cookie, connection string hoặc mật khẩu vào PR/báo cáo.

## 2. Dựng demo mới bằng Docker (khuyên dùng)

Đứng tại thư mục gốc repo:

```powershell
pwsh -NoProfile -File scripts/qa6-start-demo.ps1 -ProjectName elp-qa6-rehearsal
```

Nhập kín **hai mật khẩu khác nhau** khi được hỏi: SQL và demo. Dùng ít nhất 12 ký tự,
có chữ hoa/thường, số, ký hiệu; không dùng dấu chấm phẩy, dấu nháy kép hoặc xuống dòng
vì connection string Compose hiện ghép từ biến môi trường. Lưu chúng trong password manager.
Script cũng nhận environment nếu chạy tự động; không đặt mật khẩu literal trong lịch sử terminal.

Mở **http://localhost:18080**; `/health` phải trả 200. SQL riêng ở `127.0.0.1,15433`.
Cổng bận thì script dừng, không tắt app `8080` đang làm việc. Đổi bằng `-WebPort`/`-SqlPort`.
Project đã có container/volume cũng bị từ chối: không tự xóa hoặc reset dữ liệu.
Script build image riêng, chạy migration qua service `migrate`, rồi seed Development.
Không cần `dotnet run` hoặc EF CLI khi chạy toàn bộ bằng Compose.

| Actor | Tài khoản | Dữ liệu ban đầu |
| --- | --- | --- |
| Teacher chủ nội dung | `teacher1@demo.example.test` | Course Free/Paid/Draft và Question/Final mẫu. |
| Teacher đối chứng | `teacher2@demo.example.test` | Không sở hữu nội dung Teacher1. |
| Student làm bài | `student1@demo.example.test` | Đã Enrollment khóa Free Everyday English. |
| Student chưa Enrollment | `student2@demo.example.test` | Dùng kiểm tra bị chặn truy cập nội dung nội bộ. |
| Admin | `admin1@demo.example.test` | Không cần dùng trong demo này. |

Cả năm dùng **mật khẩu demo vừa nhập**, không phải mật khẩu SQL. Seeder không đổi mật khẩu
user đã tồn tại, không reset lịch sử và không tự tạo bài Practice mới cho kịch bản QA5.
Khóa Paid không được coi là đã thanh toán; sandbox/hosted đều tắt trong launcher này.
Đây là môi trường **local Development**, không triển khai hoặc đưa tài khoản demo lên Internet.

Container/volume còn lại sau script để bạn thao tác và giữ dữ liệu. Tạm dừng đúng project:

```powershell
docker ps -a --filter label=com.docker.compose.project=elp-qa6-rehearsal --format "{{.Names}}"
docker stop elp-qa6-rehearsal-web-1 elp-qa6-rehearsal-db-1
# Lần sau bật lại dữ liệu cũ; web có thể cần chờ SQL sẵn sàng rồi restart.
docker start elp-qa6-rehearsal-db-1
docker start elp-qa6-rehearsal-web-1
```

Tên trên dùng Compose v2 thông thường; đối chiếu kết quả `docker ps` trước khi thao tác.
Nếu web khởi động trước SQL và thoát, đợi DB healthy rồi `docker restart` đúng container web.
Không dùng launcher fresh với cùng tên để cập nhật môi trường cũ. Khi cập nhật code/migration,
dùng quy trình Compose trong README, nhập lại **đúng mật khẩu và port/image tag của project này**,
build, chạy service migrate trước web. Backup trước; không xóa volume để sửa lỗi schema.

## 3. Nếu chạy .NET local thay vì web container

Theo [README — Cách B/D](../../README.md#bắt-đầu-sau-khi-clone-hoặc-pull-repo), đặt
`ConnectionStrings__DefaultConnection` bằng environment/user-secrets của máy (không commit).
SQL chạy trong Docker của launcher ở port **15433**, không phải hostname `db` từ máy host.
Không mở hai web trên cùng cổng; audio web host có kho riêng, không tự dùng volume của container.

```powershell
dotnet restore EnglishLearningPlatform.sln
dotnet build EnglishLearningPlatform.sln -c Release
# Trong cùng terminal đã có connection string trỏ đúng DB:
dotnet run --project src/EnglishLearningPlatform.Web --no-launch-profile -- --migrate
dotnet run --project src/EnglishLearningPlatform.Web --no-launch-profile --urls http://localhost:5098
```

`--migrate` áp dụng migration đã commit rồi thoát, không tạo migration mới. Development cũng
tự migrate nếu chưa tắt `Database__ApplyMigrationsOnStartup`. Không tạo migration mới chỉ để
chạy repo vừa pull. QA2–QA6 không thêm migration; migration schema cuối hiện có là
`20261015181741_AddQuestionAndStimulusContentLocks`. Nếu dùng EF CLI, dùng phiên bản EF 10
phù hợp package runtime trong repo; lệnh kiểm tra model không cần update database:

```powershell
dotnet ef migrations has-pending-model-changes --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web
```

## 4. Chuẩn bị nội dung demo một lần (Teacher, khoảng 10–15 phút)

1. Đăng nhập Teacher1 → Ngân hàng câu hỏi → Tạo ngữ liệu. Đặt prefix `DEMO-QA6` để tìm riêng.
2. Tạo Passage: “Alex visits the library on Saturday. He borrows a book and takes the bus home.”
3. Tạo Audio bằng **upload file WAV/MP3/Ogg của bạn**, tối đa 10 MiB; tự nghe thử. Không dùng URL ngoài cho đề Publish mới.
4. Tạo 5 MCQ: ba Reading cùng passage, hai Listening cùng audio. Mỗi câu 2–4 option,
   chọn đúng một đáp án, một PrimarySkill; thêm giải thích dễ nhận biết, ví dụ `DEMO-EXPLANATION`.
   Reading có thể hỏi When/Where/How; Listening dùng câu hỏi đúng nội dung file tự chuẩn bị.
5. Ghi kín đáp án của Teacher để tính tay; không chụp đáp án Teacher vào slide kết quả Student.
6. Tạo Practice gắn **một Lesson** thuộc Free Everyday English; thêm 5 câu, lưu, Preview và Publish.
7. Tạo Assessment **PracticeExam**, Duration=20, MaxAttempts=3, PassingScore=80.
   Chọn cùng 5 câu; points lần lượt **1, 3, 8, 5, 3**. Lưu và reload kiểm tra thứ tự.
   Gắn Course Everyday English, Preview rồi Publish. Không thay Final mẫu đã có.
8. Mở Teacher2 ở browser/profile riêng để kiểm tra không được Preview nội dung Teacher1.

Muốn có bằng chứng tự động thay vì nhập tay, chạy mục 6: runner tái tạo đủ dữ liệu bằng UI.
Runner dùng môi trường tạm và **xóa dữ liệu đó sau chạy**, không phải môi trường demo giữ lại ở mục 2.

## 5. Demo ngắn sau chuẩn bị (khoảng 7–10 phút)

| Thứ tự | Actor / thao tác | Điều phải chỉ ra |
| --- | --- | --- |
| 1 | Teacher1: tìm prefix, mở Question Detail và Preview Practice/Assessment. | Passage/audio đúng; Preview không tạo lượt Student. |
| 2 | Student1: vào Course → Lesson → Practice, làm 3/5 đúng rồi làm lại 5/5. | 60% → 100%, có đáp án/giải thích; kết quả hiện tại là 100%. |
| 3 | Student1: mở Assessment, Start; chọn câu, chờ báo lưu. Thoát rồi Resume/F5. | Cùng Attempt, đáp án, deadline cũ; không tiêu thụ lượt mới do mở trang. |
| 4 | Submit lượt 1, đúng câu 1/2/3. | Overall=60%, Reading=100%; không có lời giải/đáp án từng câu. |
| 5 | Lượt 2 đúng câu 1/3/4/5; lượt 3 đúng câu 2/3/5. | 85% (Reading75%) rồi 70% (Reading91,67%); History giữ đủ ba lượt. |
| 6 | Mở Dashboard/Profile và link kết quả đại diện. | Chọn lượt85%, Reading75% cùng lượt; Listening chỉ2câu không hiện breakdown. |
| 7 | Student2 dùng URL Course-linked; Teacher1 thử sửa đề đã có Attempt. | Bị chặn đúng quyền/khóa lịch sử, không đổi điểm cũ. |

Tổng points=20; Reading=12. Nên dùng hai browser/profile Teacher/Student để tránh nhầm session.
Nếu hết MaxAttempts, **tạo/Clone đề mới hợp lệ** để demo lại; không sửa Attempt/score/deadline
hoặc xóa lịch sử bằng SQL. Không lấy Clone để thay Final đã có Attempt.
Khi ghi actual thủ công, lưu ngày/commit/actor/expected/actual/ảnh/lỗi-retest; không ký nhận thay người chưa diễn tập.

## 6. Chạy lại test và lấy bằng chứng

```powershell
dotnet build EnglishLearningPlatform.sln -c Release
dotnet test tests/EnglishLearningPlatform.UnitTests/EnglishLearningPlatform.UnitTests.csproj -c Release --no-build
# Docker Engine phải sẵn sàng; không đặt ELP_TEST_SQLSERVER vào DB đang làm việc.
dotnet test tests/EnglishLearningPlatform.IntegrationTests/EnglishLearningPlatform.IntegrationTests.csproj -c Release --no-build --settings tests/low-memory.runsettings
pwsh -NoProfile -File scripts/qa5-browser-uat.ps1 -RunName qa6-reproduce-01
```

Browser prerequisites ở mục 1; dùng RunName mới mỗi lần. Runner riêng dùng ports15055/18085,
SQL2022, startup migration/seeder, random password; tạo nội dung bằng UI và tự dọn đúng tài nguyên
của lượt đó. Report/ảnh ở `TestResults/qa5/<RunName>`. Không gọi đó là CI GitHub hoặc UAT ký tay.

## 7. Giới hạn và backup bắt buộc

- **Audio URL ngoài của đề cũ:** chỉ xác thực định dạng URL, không snapshot lại bytes đã phát.
  Bên cung cấp có thể thay/xóa file tại cùng URL. Không tuyên bố lịch sử audio ngoài là bất biến;
  không tự download để thay Stimulus đã khóa. Đề mới dùng upload và Question/Stimulus mới.
- **Final đã có Attempt:** không thay/gỡ, kể cả Clone; Archive/Unpublish cũng phải tôn trọng
  liên kết Course. Chưa có quy trình hồi tố thay Final/sửa điểm. Báo owner nếu đề đã dùng có lỗi.
- **Kho audio phải được backup cùng SQL:** named volumes `<project>_sqlserver-data` và
  `<project>_stimulus-audio-data`. Hash phát hiện hỏng/thay bytes, không phục hồi file mất.
  Không dùng `docker compose down -v`, prune hoặc xóa file audio trong quy trình cập nhật.
- Với demo đang dừng ghi: dừng web, DBA tạo SQL Server native full backup `.bak` của
  `EnglishLearningDb` và sao chép ra nơi backup; cùng thời điểm sao lưu toàn bộ audio volume
  bằng công cụ backup Docker/volume của nhóm. Ghi project, thời gian UTC, commit/schema,
  SHA256 các file backup; bảo vệ mật khẩu và dữ liệu cá nhân. Không copy các file MDF/LDF đang mở.
- Khôi phục thử sang **SQL và audio volume mới**, apply migration còn thiếu nếu đúng phiên bản,
  kiểm login, Result lịch sử và audio nội bộ bằng URL hash. Chỉ có `.bak` mà thiếu audio không đủ.
  QA6 này chưa làm diễn tập khôi phục backup production hoặc chứng nhận backup của nhóm.
- MVP audio storage là local/persistent volume; nhiều replica cần kho dùng chung bất biến.
  Tài khoản quản trị SQL/filesystem vẫn có thể sửa ngoài ứng dụng, phải kiểm soát quyền riêng.

Chi tiết: [quy tắc audio/lịch sử](../architecture/stimulus-audio-history.md).
