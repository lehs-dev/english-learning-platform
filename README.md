# English Learning Platform

**Đề tài:** Xây dựng nền tảng học tập và đánh giá năng lực tiếng Anh trực tuyến trên nền web bằng ASP.NET Core.

**Bàn giao Practice–Assessment–Result (21/10/2026):** xem [QA6: chạy/config/migration và demo](docs/handover/qa6-run-demo.md)
và [testcase, actual, lỗi/retest và gói bằng chứng](docs/acceptance/qa6-handover-results.md).
Có launcher demo Windows riêng, không reset database đang làm việc; kết quả local không thay CI/rehearsal nhóm.
Kết quả sau cập nhật main: [kiểm tra trước push QA2–QA6](docs/acceptance/qa-pre-push-20261021.md).

Muốn khám phá nhiều Course, Module, Lesson, Practice, Placement/Skill/Mock Assessment và audio mẫu trên UI, bật [bộ dữ liệu demo mở rộng](docs/project/demo-seed-data.md) trong Development.

> **Cập nhật Công việc 10–12 (11/10/2026):** Có giao diện Final Assessment (Start/Save/Resume/Submit/Result), huy hiệu hoàn thành khóa, Admin tra cứu giao dịch/tài khoản và bộ hồ sơ bàn giao trong [docs/handover](docs/handover/README.md). Trạng thái kiểm thử, CI và rehearsal được ghi riêng theo bằng chứng thực tế; không suy ra mọi phân hệ của dự án đã hoàn tất.

**Cập nhật 03/10/2026:** Đã có catalog Course công khai, tìm/lọc/phân trang, detail chỉ metadata/cấu trúc/giá, enroll Free, đọc Lesson/resource có quyền và giao diện Teacher authoring/preview/Publish/Unpublish/Archive. Paid hiện có schema và kiểm tra Enrollment/Payment; checkout/webhook chưa triển khai. Xem [hướng dẫn Course và dữ liệu demo](docs/project/course-authoring.md).

**Cập nhật 08/10/2026:** Đã có Order/hosted checkout, webhook ký HMAC xác minh phía server, chống Enrollment trùng, My Courses có progress, trang Module/Lesson và Completed/Incomplete. Áp dụng migration `AddCheckoutOrderMetadata`. Thanh toán mặc định tắt và chưa nối provider thật; xem [cấu hình và hợp đồng checkout](docs/architecture/payment-sandbox.md). Progress chỉ tính Lesson Published trong Module Visible; không có bài thì không tính phần trăm; Archived chỉ đọc. Chi tiết ở [hướng dẫn Learning](docs/project/course-authoring.md).

**Checkout hợp nhất:** Hosted checkout/metadata Order và progress của main dùng chung service với UI Estudy và sandbox local. Hosted được ưu tiên khi cấu hình sẵn sàng; sandbox là chế độ opt-in chỉ Development, không thu tiền thật và không thể xác nhận Order hosted. Helper bảo vệ begin/commit/rollback/dispose, cancellation và callback replay. Xem [sandbox local](docs/project/checkout-local.md) và [hợp đồng hosted](docs/architecture/payment-sandbox.md). Chưa thử provider thật hoặc pair với Sơn/Khánh.

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
- `tests/`: unit test và integration test. CI chạy **unit test và integration test** trên PR/push vào `main`; trước khi mở PR hãy chạy các test liên quan tại máy của mình.

CD hiện chỉ build/push image lên **GitHub Container Registry**, chưa tự triển khai website lên VPS/Azure.

## Bắt đầu sau khi clone hoặc pull repo

Cần Git và Docker có Docker Compose. Nếu chạy ứng dụng trên máy host, cài thêm .NET 10 SDK. Repo dùng SQL Server 2022; các migration khởi tạo schema và seed ba role (**Student**, **Teacher**, **Admin**) đã được commit. Database cần áp dụng migration khi tạo mới hoặc khi code có migration mới; EF Core chỉ áp dụng migration còn thiếu.

Khi chạy với `ASPNETCORE_ENVIRONMENT=Development`, web mặc định tự áp dụng migration còn thiếu **trước khi seed dữ liệu hoặc nhận request**. Cơ chế này dùng chung cho Windows local, Linux/macOS, Dev Container và Compose Development, nên sau khi pull code mới, chỉ cần khởi động lại web với đúng connection string. Database phải sẵn sàng; nếu cập nhật thất bại, web dừng khởi động để tránh lỗi thiếu cột khi đang làm bài. Đặt `Database__ApplyMigrationsOnStartup=false` nếu muốn tự quản lý migration bằng các lệnh bên dưới. Production luôn dùng bước `--migrate` riêng, kể cả khi cờ này được bật.

### Cách A: VS Code Dev Container

Cần VS Code và extension Dev Containers. Mở repo trong VS Code, chọn **Dev Containers: Reopen in Container**. Compose chờ SQL Server healthy trước khi mở môi trường .NET. Có thể xem log từ terminal trên máy host:

    docker compose -f .devcontainer/docker-compose.yml logs -f db

Nhấn **Ctrl+C** để thoát xem log, không dừng database. Mở terminal tích hợp trong Dev Container và chạy:

    dotnet ef database update --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web
    dotnet run --project src/EnglishLearningPlatform.Web

Trong Dev Container, hostname database là **db**. Mở **http://localhost:8080** trên máy host. Database được lưu trong volume **sqlserver-dev-data**; lần sau mở lại container thì không cần tạo lại. Sau khi pull migration mới, khởi động lại web để cập nhật schema.

Lệnh EF ở trên là bước cập nhật thủ công tùy chọn trong Development; `dotnet run` cũng tự cập nhật schema khi khởi động. Nếu host đã dùng cổng 1433, đặt `ELP_SQL_PORT` thành cổng trống trước khi mở Dev Container; kết nối bên trong container vẫn dùng `db,1433`.

### Cách B: Ứng dụng local, SQL Server trong Docker

Cách này chạy .NET trên máy host và chỉ chạy database trong Docker. Cần cài .NET 10 SDK. Trong thư mục gốc repo, tạo file cấu hình local (chỉ cần lần đầu):

    cp .env.example .env

Trên PowerShell dùng **Copy-Item .env.example .env**. Mở **.env**, đặt **MSSQL_SA_PASSWORD** riêng cho máy của bạn. Không commit file **.env** hoặc mật khẩu.

Khởi động database:

    docker compose up -d db
    docker compose logs -f db

Chờ log SQL Server báo đã sẵn sàng rồi nhấn **Ctrl+C** để thoát xem log (container vẫn chạy). Cài công cụ EF Core một lần nếu máy chưa có:

    dotnet tool install --global dotnet-ef --version '10.*'

Đặt connection string cho lệnh EF và web chạy trên máy host. Thay **YOUR_MSSQL_SA_PASSWORD** bằng đúng mật khẩu trong **.env**.

Linux/macOS, Git Bash hoặc WSL:

    export ConnectionStrings__DefaultConnection='Server=127.0.0.1,1433;Database=EnglishLearningDb;User Id=sa;Password=YOUR_MSSQL_SA_PASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=true'

PowerShell:

    $env:ConnectionStrings__DefaultConnection = 'Server=127.0.0.1,1433;Database=EnglishLearningDb;User Id=sa;Password=YOUR_MSSQL_SA_PASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=true'

Áp dụng các migration đã có trong repo:

    dotnet ef database update --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web

Chạy lệnh này khi tạo database local mới và sau khi pull thay đổi có migration. Nếu database đã cập nhật, lệnh kết thúc mà không thay đổi schema. Sau đó chạy web trong cùng terminal để giữ connection string:

    dotnet restore EnglishLearningPlatform.sln
    dotnet run --project src/EnglishLearningPlatform.Web

Mở URL được in trong terminal. Ứng dụng local kết nối tới **127.0.0.1,1433**, khớp địa chỉ IPv4 Compose đã bind và tránh timeout khi `localhost` được phân giải thành IPv6. Nếu đặt `ELP_SQL_PORT` khác 1433, đổi cổng trong connection string cho khớp.

### Cách C: Ứng dụng và database cùng chạy trong Docker Compose

Thực hiện Cách B từ bước tạo **.env** đến hết lệnh **dotnet ef database update**; không chạy **dotnet run** trên host. Lệnh EF dùng **127.0.0.1,1433**, còn web trong Compose tự dùng hostname **db**. Sau đó chạy:

    docker compose up --build -d

Đặt `MSSQL_SA_PASSWORD` trong environment hoặc `.env` local (không commit). Compose chờ SQL healthy, chạy service `migrate` một lần rồi mới mở `web`; cập nhật schema không cần EF CLI trên host. Khi pull migration mới, chạy `docker compose run --rm migrate` trước khi mở lại web. Mở **http://localhost:8080**; `docker compose ps` và **http://localhost:8080/health** kiểm tra readiness. Image chạy bằng user `app`. `ELP_SQL_PORT` và `ELP_WEB_PORT` đổi cổng khi cần; cổng mặc định chỉ bind localhost.

Demo dữ liệu và checkout local chỉ bật khi opt-in Development: xem [hướng dẫn demo](docs/project/son-demo-fallback.md). `bash scripts/fresh-demo.sh` tạo project/volume riêng, từ chối tên project đã có dữ liệu. [Browser UAT](tests/browser/README.md) chạy Guest → thanh toán → học → Final → kết quả → Teacher → Admin và lưu ảnh không chứa secret.

Dừng container nhưng giữ dữ liệu bằng **docker compose down** hoặc **docker compose stop**. Volume **sqlserver-data** giữ database qua các lần dừng/chạy. Chỉ dùng **docker compose down -v** nếu chủ động muốn xóa database local.

### Cách D: SQL Server cài trực tiếp trên Windows

Cần .NET 10 SDK và SQL Server local. Nếu dùng instance mặc định `localhost` với Windows Authentication, dùng cùng profile `LocalSqlServer` để cập nhật database rồi chạy web:

```powershell
dotnet run --project src/EnglishLearningPlatform.Web --launch-profile LocalSqlServer -- --migrate
dotnet run --project src/EnglishLearningPlatform.Web --launch-profile LocalSqlServer
```

Mở **http://localhost:5098**. Profile `LocalSqlServer` trỏ tới database **EnglishLearningDb** trên instance mặc định; chọn profile này khi chạy Windows local. Nếu dùng named instance hoặc SQL Authentication, đặt connection string phù hợp và chạy profile `http` để giữ cấu hình bạn đã đặt.

Có thể chạy riêng lệnh `--migrate` sau khi pull code có migration mới. Lệnh chỉ áp dụng migration còn thiếu rồi thoát; không mở web và không cần cài `dotnet-ef`. Nếu nộp bài hoặc bấm **Hoàn tất lượt đã hết giờ** báo `Invalid column name 'ContentLockedAtUtc'`, database còn thiếu migration `20261015181741_AddQuestionAndStimulusContentLocks`. Khởi động lại web Development để tự cập nhật, hoặc chạy lệnh cập nhật ở trên nếu đã tắt migration khi khởi động; migration thêm cột cho `Questions` và `Stimuli`, ghi nhận khóa nội dung cũ và giữ nguyên bài làm, đáp án, điểm số. Không cần tạo lại database.

Trang đăng nhập có ba nút Student/Teacher/Admin để chọn tài khoản demo. Trong Development, các nút tự điền cả mật khẩu từ `LearningDemo:Password` trong user-secrets hoặc biến môi trường `LearningDemo__Password`; giá trị này phải trùng mật khẩu của các tài khoản đã seed. User-secrets chỉ lưu trên máy local. Trang Production không xuất mật khẩu demo.

`dotnet ef` không đọc `launchSettings.json` hay profile `LocalSqlServer`. Vì vậy, phải đặt biến `ConnectionStrings__DefaultConnection` trước lệnh EF trong cùng terminal, hoặc dùng user-secrets. `appsettings.json` không chứa connection string hoặc mật khẩu mặc định; bỏ bước cấu hình sẽ không thể kết nối database.

Trước khi build hoặc cập nhật database trên Windows, dừng ứng dụng đang chạy bằng **Ctrl+C** trong terminal của `dotnet run` hoặc Stop Debugging trong VS Code. Nếu app đang giữ DLL, EF có thể báo `Build failed` và build báo MSB3021/MSB3027. Khi đã build thành công, có thể thêm `--no-build` cho lệnh `dotnet ef database update`; chạy web sau khi cập nhật database xong.

Cách A/B vẫn dùng `dotnet run --project src/EnglishLearningPlatform.Web` (profile `http`), nhận connection string và URL từ environment. Cách C chạy DLL đã publish; không dùng launch profile. Không chọn `LocalSqlServer` trong Docker/Dev Container vì profile đó dành cho Windows Authentication trên máy host.

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
- [Profile, UI và quyền Learning](docs/project/profile-ui.md) — cấu trúc code, phạm vi giao diện và cách kiểm thử.
- [Backlog tham khảo](docs/project/backlog.csv) và [GitHub Project](docs/project/github-project.md).
- [Kế hoạch luyện vấn đáp](docs/project/defense-readiness.md) và [thiết lập Fedora Dev Container](docs/setup/fedora-devcontainer.md).

GitHub issue/PR là nơi theo dõi việc đang thực hiện; bảng tiến độ và báo cáo môn học lấy bằng chứng từ đó và được cập nhật theo mốc nhóm thống nhất.

**Lưu ý khi đưa README vào repo:** `docs/project/git-workflow.md` cũ vẫn hướng dẫn dùng `develop`. Quy trình đang áp dụng nằm trong README này (`feature/fix/docs` → PR vào `main`); hãy đồng bộ tài liệu Git cũ trước khi cho nhóm dùng làm hướng dẫn.
