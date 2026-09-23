# English Learning Platform

**Đề tài:** Xây dựng nền tảng học tập và đánh giá năng lực tiếng Anh trực tuyến trên nền web bằng ASP.NET Core.

> **Tình trạng mã nguồn (23/09/2026):** `main` mới có khung solution, một số entity/EF mapping, cấu hình Identity, trang Home, health check, scoring service và test cơ bản. Chưa có migration được commit hoặc luồng sản phẩm hoàn chỉnh. Các mục dưới đây mô tả **sản phẩm cần xây**, không có nghĩa là chức năng đã xong. Xem issue/PR để biết tiến độ thực tế.

## Sản phẩm cần xây

| Người dùng | Luồng chính |
| --- | --- |
| Guest | Xem danh mục/chi tiết khóa công khai, đăng ký tài khoản Student, đăng nhập. Guest chưa xác thực, không phải role trong database. |
| Student | Chọn khóa và enroll → học theo Module/Lesson → đánh dấu hoàn thành → luyện tập → làm bài đánh giá → xem điểm, tiến độ và gợi ý học tiếp. |
| Teacher | Tạo và quản lý khóa học, Module, Lesson; quản lý ngân hàng câu hỏi; tạo Practice/Assessment; xem kết quả của nội dung mình sở hữu. |
| Admin | Quản lý tài khoản, role, trạng thái và thao tác quản trị; không sửa nội dung học tập của Teacher. |

Trong phạm vi hiện tại, mỗi tài khoản có một role đang hoạt động: `Student`, `Teacher` hoặc `Admin`. Teacher chỉ thay đổi tài nguyên mình sở hữu; mọi kiểm tra quyền phải được thực hiện ở server.

### Các luồng nghiệp vụ cần phân biệt

- **Learning:** `Course → Module → Lesson`. Student đã enroll được mở mọi Lesson đang khả dụng, không bắt buộc học tuần tự. Student tự đánh dấu Lesson `Completed/Incomplete`. Progress được tính trên các Lesson khả dụng; nếu không có Lesson khả dụng, hiển thị “Chưa có nội dung khả dụng”.
- **Practice:** Bài luyện gắn với Lesson hoặc Module. Student có thể làm lại không giới hạn; chấm đều theo số câu đúng, lưu kết quả gần nhất và xem đáp án/giải thích sau khi nộp.
- **Assessment:** Bài đánh giá có attempt chính thức, deadline do server quản lý, giới hạn lượt làm theo cấu hình, chấm điểm theo trọng số câu hỏi và lưu lịch sử. Kết quả gồm điểm tổng, điểm theo kỹ năng và kỹ năng cần cải thiện; không hiện đáp án chi tiết của từng câu trong Assessment. Các loại cần hỗ trợ là Placement Test, Skill Assessment và Mock/Practice Exam.

Mỗi Question của MVP là trắc nghiệm chọn **một** đáp án đúng, có một `PrimarySkill` trong Reading, Listening, Vocabulary, Grammar. Kết quả Placement không tự nhận là chứng nhận CEFR/TOEIC. Các ý tưởng như thanh toán, diễn đàn, livestream hoặc chấm Speaking/Writing bằng AI nằm ngoài phạm vi hiện tại.

### Các mốc bàn giao

| Mốc | Bằng chứng cần có |
| --- | --- |
| Tuần 7 — bản demo môn Công nghệ hiện đại | Chạy ổn định luồng đăng ký/đăng nhập → chọn khóa → học Lesson → làm bài đánh giá → nộp → chấm → xem kết quả và tiến độ; có test, CI, PR/review và mỗi người trong nhóm vấn đáp tự giải thích được luồng. |
| Tuần 13 — bản nộp .NET và QLDA | Hoàn thiện phạm vi đã thống nhất, giao diện và dữ liệu demo, kiểm thử liên module, tài liệu/báo cáo, slide, minh chứng tiến độ và bàn giao mã nguồn. |

Ưu tiên hoàn thành một luồng xuyên suốt trước các mở rộng. Mỗi chức năng cụ thể cần có issue với tiêu chí nghiệm thu, người phụ trách và mốc hoàn thành.

## Công nghệ và cấu trúc repo

- .NET 10, ASP.NET Core MVC, EF Core 10, ASP.NET Core Identity, SQL Server 2022.
- xUnit cho test; Docker Compose cho môi trường; GitHub Actions cho CI và đóng gói container lên GHCR.
- `src/EnglishLearningPlatform.Domain`: entity, enum, quy tắc lõi.
- `src/EnglishLearningPlatform.Application`: interface, DTO và service nghiệp vụ.
- `src/EnglishLearningPlatform.Infrastructure`: EF Core, SQL Server, Identity và tích hợp hạ tầng.
- `src/EnglishLearningPlatform.Web`: controller, middleware, view và static assets.
- `tests/`: unit test và integration test. CI hiện chạy **unit test** trên PR/push vào `main`; trước khi mở PR hãy chạy các test liên quan tại máy của mình.

CD hiện chỉ build/push image lên **GitHub Container Registry**, chưa tự triển khai website lên VPS/Azure.

## Bắt đầu trên máy của mình

### Cách A: VS Code Dev Container

Nếu dùng VS Code với Docker, clone repo rồi chọn **Dev Containers: Reopen in Container**. Container phát triển có .NET và SQL Server; hướng dẫn cho Fedora tại [`docs/setup/fedora-devcontainer.md`](docs/setup/fedora-devcontainer.md). Sau khi container khởi tạo:

```bash
./scripts/dev-check.sh
dotnet run --project src/EnglishLearningPlatform.Web
```

### Cách B: .NET trên máy, SQL Server trong Docker

Cần .NET 10 SDK, Git và Docker Compose. Thực hiện trong thư mục gốc của repo:

```bash
cp .env.example .env
# Sửa MSSQL_SA_PASSWORD trong .env cho môi trường local; không commit .env.
docker compose up -d db

# Thay YOUR_LOCAL_PASSWORD bằng đúng giá trị MSSQL_SA_PASSWORD trong .env.
export ConnectionStrings__DefaultConnection='Server=localhost,1433;Database=EnglishLearningDb;User Id=sa;Password=YOUR_LOCAL_PASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=true'

dotnet restore EnglishLearningPlatform.sln
dotnet build EnglishLearningPlatform.sln --no-restore
dotnet test EnglishLearningPlatform.sln --no-build
dotnet run --project src/EnglishLearningPlatform.Web
```

`docker compose` đọc `.env`; `dotnet run` trên máy **không tự đọc** file này, vì vậy cần cấu hình connection string qua biến môi trường như trên (hoặc cơ chế cấu hình local tương đương). Mở địa chỉ mà `dotnet run` in ra. Route kiểm tra là `/health`; health check cần kết nối được SQL Server. Chỉ dùng mật khẩu mẫu cho máy phát triển, không dùng khi triển khai.

**Migration:** Hiện repo chưa có `InitialCreate`. Người được giao quản lý schema sẽ chốt ERD, tạo migration và đưa vào PR. Thành viên khác không tự tạo migration đầu tiên. Khi migration đã được merge, cài `dotnet-ef` bản 10 nếu cần, rồi áp dụng migration đã commit:

```bash
dotnet tool install --global dotnet-ef --version '10.*'
dotnet ef database update \
  --project src/EnglishLearningPlatform.Infrastructure \
  --startup-project src/EnglishLearningPlatform.Web
```

Khi schema và migration đã sẵn sàng, có thể chạy cả web lẫn database bằng `docker compose up --build`; web ở `http://localhost:8080`. Compose không tự chạy migration thay bạn.

## Một phiên làm việc của thành viên

Mỗi phiên nên kết thúc bằng **một thay đổi có thể kiểm tra được** (commit/PR hoặc kết quả thử nghiệm) **và một cập nhật tiến độ**. Nếu bị chặn, ghi nguyên nhân và người cần hỗ trợ; không đánh dấu Done theo số giờ đã làm.

### 1. Nhận và hiểu việc trước khi sửa mã

1. Mở issue được giao: đọc mục tiêu, tiêu chí nghiệm thu (AC), deadline và các issue phụ thuộc. Nếu chưa có issue, tạo hoặc nhờ trưởng nhóm chốt issue trước khi code.
2. Đối chiếu quy tắc nghiệp vụ trong issue với màn hình/API, quyền Student/Teacher/Admin và dữ liệu cần lưu. Ghi câu hỏi vào issue nếu đặc tả hoặc ERD còn mâu thuẫn; chốt hợp đồng dữ liệu với người phụ trách module liên quan.
3. Chọn một phần việc đủ nhỏ để kiểm tra trong phiên: ví dụ một hành vi, một validation, một bug kèm test. Xác nhận ai review và ai giữ migration nếu có thay đổi schema.

### 2. Cập nhật nhánh và làm việc

Kiểm tra `git status` trước; lưu hoặc hoàn tất thay đổi đang dở rồi mới đổi nhánh. Với issue mới:

```bash
git switch main
git pull --ff-only origin main
git switch -c feature/123-course-enrollment
```

Đổi `123-course-enrollment` thành số issue và tên việc thật. Với nhánh đang làm dở, chuyển sang đúng nhánh đó rồi lấy thay đổi mới từ `main` theo cách nhóm đã thống nhất. Dùng `fix/<issue>-<ten-ngan>` cho bug và `docs/<issue>-<ten-ngan>` cho tài liệu. Không commit trực tiếp vào `main`.

- Sửa đúng phạm vi issue, commit từng phần có ý nghĩa. Controller không nên chứa toàn bộ logic nghiệp vụ; kiểm tra authorization và validation ở server.
- Nếu đụng tới Course/Module/Lesson, Practice/Assessment, Identity hay FK/index, trao đổi với owner của module và người giữ schema **trước** khi đổi contract hoặc migration.
- Viết test cho quy tắc quan trọng, đặc biệt các trường hợp sai quyền, dữ liệu trùng, deadline/submit lặp và tính điểm. Với UI, tự đi qua luồng bằng role và dữ liệu demo phù hợp.
- Ghi lại lệnh đã chạy, kết quả và ảnh/chứng cứ demo khi issue yêu cầu; không đưa mật khẩu, `.env` hay dữ liệu riêng tư vào commit/log/chụp màn hình.

### 3. Tự kiểm tra và bàn giao cuối phiên

```bash
git diff --check
dotnet build EnglishLearningPlatform.sln
dotnet test EnglishLearningPlatform.sln
git status
```

Nếu một test hoặc bước chạy tay thất bại, sửa hoặc ghi rõ lỗi và cách tái hiện trên issue/PR. Khi thay đổi đã sẵn sàng:

1. Commit có nội dung rõ, ví dụ `feat(learning): add enrollment validation`; push nhánh (`git push -u origin HEAD`).
2. Mở PR vào **`main`** theo [PR template](.github/pull_request_template.md): liên kết issue (`Closes #123` khi thực sự giải quyết xong), liệt kê thay đổi, lệnh/kết quả test, cách thử tay, ảnh UI nếu cần, migration và ảnh hưởng tới module khác.
3. Theo dõi CI, sửa lỗi, nhờ **một thành viên khác** review. Người review thử các AC, quyền truy cập và hiểu đủ logic để giải thích khi vấn đáp. Chỉ merge khi review xong và CI xanh.
4. Cập nhật issue/bảng tiến độ: trạng thái, link commit/PR, AC đã đạt, việc còn dở và người cần tiếp nhận. Nếu còn dở, đẩy nhánh hoặc mở draft PR, ghi blocker và bước tiếp theo; giữ trạng thái In Progress.

**Ví dụ ghi cuối phiên:** `Issue #123 — đã chặn enroll trùng, test X/Y đạt, PR #45 đang chờ Khánh review; còn kiểm tra 403 với Teacher trước ngày 27/9.`

### Khi nào được đánh dấu Done?

- Mọi AC của issue có bằng chứng kiểm tra; dữ liệu, quyền và trường hợp lỗi liên quan đã được thử.
- Build/test liên quan chạy được; CI của PR xanh; thay đổi schema có migration và hướng dẫn áp dụng nếu cần.
- PR đã được thành viên khác review/merge; issue và bảng tiến độ có link, trạng thái và ghi chú bàn giao chính xác.

## Phân công và trao đổi chéo

| Người | Trách nhiệm chính | Cần phối hợp với |
| --- | --- | --- |
| Sơn (trưởng nhóm) | Kiến trúc, Identity/Authorization, tích hợp, toàn vẹn dữ liệu, CI/CD và release. | Khánh/Hoàng khi đổi schema hoặc contract; Khang/Dương để kiểm thử luồng. |
| Khánh | Learning: Course, Module, Lesson, Enrollment, Progress và recommendation. | Hoàng ở liên kết bài học/bài đánh giá; Khang ở UI. |
| Hoàng | Question Bank, Practice, Assessment, Attempt, Scoring và Result. | Khánh ở Course/Final; Sơn ở transaction/quyền; Khang ở UI. |
| Khang | UI, Admin, dữ liệu demo, QA, tài liệu và hỗ trợ báo cáo. | Các owner API để thử trọn luồng bốn actor. |
| Dương | Code chéo, test tự động, tích hợp và chuẩn bị vấn đáp. | Pair/review với các owner; ghi rõ phần đã làm trong issue/PR. |

Không chờ đến tuần demo mới thử phần của người khác. Sau mỗi tuần, mỗi owner trình bày luồng đang làm được; một người khác clone, chạy và nêu được request → service → database → kết quả.

## Tài liệu liên quan

- [Kiến trúc](docs/architecture/architecture.md) và [ERD nghiệp vụ hiện có](docs/architecture/erd.md) — đối chiếu với phạm vi ở README/issue trước khi chốt migration mới.
- [Backlog tham khảo](docs/project/backlog.csv) và [GitHub Project](docs/project/github-project.md).
- [Kế hoạch luyện vấn đáp](docs/project/defense-readiness.md) và [thiết lập Fedora Dev Container](docs/setup/fedora-devcontainer.md).

GitHub issue/PR là nơi theo dõi việc đang thực hiện; bảng tiến độ và báo cáo môn học lấy bằng chứng từ đó và được cập nhật theo mốc nhóm thống nhất.

**Lưu ý khi đưa README vào repo:** `docs/project/git-workflow.md` cũ vẫn hướng dẫn dùng `develop`. Quy trình đang áp dụng nằm trong README này (`feature/fix/docs` → PR vào `main`); hãy đồng bộ tài liệu Git cũ trước khi cho nhóm dùng làm hướng dẫn.
