# English Learning Platform (ELP)

Nền tảng học tập và đánh giá năng lực tiếng Anh trực tuyến được xây dựng trên nền tảng **ASP.NET Core (.NET 10)** theo mô hình kiến trúc phân lớp (Layered Architecture).

---

## 1. Giới thiệu dự án

Hệ thống cung cấp giải pháp toàn diện cho việc đào tạo, luyện tập và khảo thí tiếng Anh trực tuyến với 3 nhóm người dùng chính (**Student**, **Teacher**, **Admin**) và đối tượng khách vãng lai (**Guest**).

### Các phân hệ & tính năng chính

Học viên có thể học tiếp từ trang Hôm nay, nộp bài viết để nhận phản hồi, hỏi riêng giảng viên và theo dõi streak theo ngày Việt Nam. Giáo viên có thể giao bài viết, tải ảnh và audio vào bài học, nhập flashcard từ CSV và tạo câu nghe chép chính tả cho bài luyện hoặc bài thi. Xem [báo cáo rà soát và lộ trình nâng cấp](docs/platform-upgrade-roadmap.md).

- **Learning (Học tập)**:
  - Khóa học được tổ chức theo cấu trúc phân cấp: `Course → Module → Lesson`.
  - Học viên theo dõi tiến độ học tập (`Progress`), tự đánh dấu hoàn thành bài học (`Completed / Incomplete`).
  - Hỗ trợ học liệu đa phương tiện (văn bản, âm thanh, hình ảnh).
- **Practice (Luyện tập)**:
  - Bài luyện tập gắn liền với từng bài học hoặc chương học.
  - Cho phép học viên làm lại không giới hạn số lần, hiển thị kết quả và giải thích chi tiết ngay sau khi nộp bài.
- **Assessment (Đánh giá & Khảo thí)**:
  - Hỗ trợ đa dạng loại bài kiểm tra: **Placement Test** (xếp lớp), **Skill Assessment** (đánh giá kỹ năng), **Mock Exam** (thi thử) và **Final Assessment** (bài thi kết thúc khóa học).
  - Quản lý thời gian làm bài phía máy chủ (server timer), tự động lưu bài làm (auto-save), bảo vệ nội dung thi.
  - Chấm điểm tự động theo trọng số câu hỏi; phân tích kết quả chi tiết theo 4 kỹ năng: **Reading** (kèm văn bản đọc), **Listening** (kèm file âm thanh), **Vocabulary**, **Grammar**.
- **Question Bank (Ngân hàng câu hỏi)**:
  - Quản lý danh mục câu hỏi phong phú, hỗ trợ gắn ngữ liệu đọc và file âm thanh (Stimulus Audio / Text).
  - Câu hỏi hỗ trợ phân loại theo kỹ năng chính, độ khó và trạng thái xuất bản.
- **Commerce & Payment (Thanh toán & Ghi danh)**:
  - Hỗ trợ cả khóa học miễn phí (Free Enrollment) và khóa học trả phí (Paid Course).
  - Tích hợp cổng thanh toán VNPay Sandbox & Hosted Checkout, bảo mật chữ ký HMAC và chống ghi nhận trùng lặp giao dịch.
- **Phân quyền người dùng (Role-Based Access Control)**:
  - **Guest**: Xem danh mục khóa học công khai, chi tiết khóa học, đăng ký tài khoản học viên và đăng nhập.
  - **Student**: Đăng ký khóa học, học bài, làm bài luyện tập, tham gia thi đánh giá và xem lịch sử kết quả.
  - **Teacher**: Quản lý khóa học, module, bài học do mình sở hữu; quản lý ngân hàng câu hỏi và học liệu nghe/đọc; thiết lập bài Practice và Assessment.
  - **Admin**: Quản lý tài khoản người dùng, phân quyền vai trò, quản trị hệ thống và tra cứu lịch sử giao dịch.

---

## 2. Công nghệ sử dụng & Cấu trúc dự án

### Công nghệ

- **Backend**: .NET 10, ASP.NET Core MVC, Entity Framework Core 10, ASP.NET Core Identity.
- **Cơ sở dữ liệu**: Microsoft SQL Server 2022.
- **Testing**: xUnit, Playwright.
- **Containerization**: Docker, Docker Compose.

### Cấu trúc mã nguồn

```text
├── src/
│   ├── EnglishLearningPlatform.Domain/          # Entity, Enum, Value Objects, Domain Exceptions
│   ├── EnglishLearningPlatform.Application/     # Interface, DTO, Service nghiệp vụ
│   ├── EnglishLearningPlatform.Infrastructure/  # EF Core, Identity, Persistence, Migrations, Seeders
│   └── EnglishLearningPlatform.Web/             # MVC Controllers, Views, ViewModels, Middleware
├── tests/
│   ├── EnglishLearningPlatform.UnitTests/       # Unit tests (xUnit)
│   ├── EnglishLearningPlatform.IntegrationTests/# Integration tests với database
│   └── browser/                                 # Kiểm thử UI/E2E với Playwright
├── scripts/                                     # Các script hỗ trợ phát triển và kiểm thử
├── docker-compose.yml                           # Docker Compose cho Database, Migration và Web
└── Dockerfile                                   # Multi-stage Dockerfile cho ứng dụng Web
```

---

## 3. Hướng dẫn chạy dự án

### Yêu cầu môi trường

- **Cách 1 (Khuyên dùng)**: [Docker Desktop](https://www.docker.com/) hoặc Docker Engine có hỗ trợ Docker Compose.
- **Cách 2**: [.NET 10 SDK](https://dotnet.microsoft.com/download) và Microsoft SQL Server 2022 (hoặc SQL Server chạy trong Docker).

---

### Cách 1: Chạy toàn bộ bằng Docker Compose (Nhanh nhất)

1. **Chuẩn bị cấu hình môi trường**:
   Sao chép file cấu hình mẫu `.env.example` thành `.env`:
   ```bash
   cp .env.example .env
   ```
   Mở file `.env` và thiết lập mật khẩu SQL Server cùng mật khẩu cho dữ liệu demo:
   ```env
   MSSQL_SA_PASSWORD=<your-strong-password>
   LearningDemo__Enabled=true
   LearningDemo__Password=<your-demo-password>
   ```

2. **Khởi động ứng dụng**:
   ```bash
   docker compose up --build -d
   ```
   Docker Compose sẽ tự động:
   - Khởi chạy SQL Server và chờ trạng thái *healthy*.
   - Chạy container `migrate` để cập nhật schema cơ sở dữ liệu.
   - Khởi chạy dịch vụ `web` và seed dữ liệu demo (nếu `LearningDemo__Enabled=true`).

3. **Truy cập ứng dụng**:
   - Web Application: **http://localhost:8080**
   - Kiểm tra trạng thái: **http://localhost:8080/health**

4. **Dừng ứng dụng**:
   ```bash
   docker compose down
   ```
   *(Nếu muốn xóa sạch toàn bộ dữ liệu cơ sở dữ liệu để làm mới, thêm cờ `-v`: `docker compose down -v`)*.

---

### Cách 2: Chạy Local với .NET SDK (SQL Server trên Docker)

1. **Khởi chạy SQL Server trong Docker**:
   ```bash
   cp .env.example .env
   # Đặt MSSQL_SA_PASSWORD trong file .env
   docker compose up -d db
   ```

2. **Cài đặt công cụ EF Core CLI** (nếu máy chưa cài):
   ```bash
   dotnet tool install --global dotnet-ef --version '10.*'
   ```

3. **Cấu hình biến môi trường kết nối**:
   - **Linux / macOS / WSL**:
     ```bash
     export ConnectionStrings__DefaultConnection='Server=127.0.0.1,1433;Database=EnglishLearningDb;User Id=sa;Password=YOUR_MSSQL_SA_PASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=true'
     export ASPNETCORE_ENVIRONMENT=Development
     export LearningDemo__Enabled=true
     export LearningDemo__Password='<your-demo-password>'
     ```
   - **Windows PowerShell**:
     ```powershell
     $env:ConnectionStrings__DefaultConnection = 'Server=127.0.0.1,1433;Database=EnglishLearningDb;User Id=sa;Password=YOUR_MSSQL_SA_PASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=true'
     $env:ASPNETCORE_ENVIRONMENT = 'Development'
     $env:LearningDemo__Enabled = 'true'
     $env:LearningDemo__Password = '<your-demo-password>'
     ```

4. **Áp dụng Database Migration**:
   ```bash
   dotnet ef database update --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web
   ```

5. **Khởi chạy ứng dụng Web**:
   ```bash
   dotnet run --project src/EnglishLearningPlatform.Web
   ```
   Mở trình duyệt theo địa chỉ được thông báo trên terminal (mặc định thường là **http://localhost:5000** hoặc **http://localhost:5098**).

---

## 4. Hướng dẫn Seed dữ liệu mẫu (Demo Data)

### Cách kích hoạt Seed dữ liệu

1. **Tự động seed khi khởi động (Development)**:
   Bật `LearningDemo__Enabled=true` trong môi trường `Development`. Khi cần tạo tài khoản mẫu, đặt thêm `LearningDemo__Password`. Docker Compose đọc các giá trị này từ tệp `.env`. Ứng dụng áp dụng migration theo cấu hình khởi động.

2. **Chạy seed độc lập qua CLI**:
   Với database đã có đủ năm tài khoản mẫu, chạy lệnh sau để cập nhật dữ liệu và giữ mật khẩu hiện tại.

   ```bash
   dotnet run --project src/EnglishLearningPlatform.Web -- --seed-demo
   ```

   Nếu còn thiếu tài khoản mẫu, đặt mật khẩu bằng user-secrets của dự án Web trước khi chạy lệnh trên. Mật khẩu cần ít nhất 8 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt.

   ```bash
   dotnet user-secrets set 'LearningDemo:Password' '<mật-khẩu-mẫu>' --project src/EnglishLearningPlatform.Web
   ```

   Khi dùng `dotnet run`, cấu hình được lấy từ user-secrets hoặc biến môi trường của terminal. Tệp `.env` dùng cho Docker Compose.

---

### Danh sách tài khoản Demo mặc định

Tài khoản mới dùng mật khẩu trong `LearningDemo:Password`. Tài khoản đã có tiếp tục dùng mật khẩu được đặt khi tạo.

| Vai trò | Email đăng nhập | Mục đích sử dụng |
| --- | --- | --- |
| Quản trị viên | `admin1@demo.example.test` | Quản lý tài khoản, phân quyền, tra cứu giao dịch |
| Giảng viên | `teacher1@demo.example.test` | Quản lý danh mục mẫu, ngân hàng câu hỏi và đề thi |
| Giảng viên | `teacher2@demo.example.test` | Quản lý khóa **Tiếng Anh tại quán cà phê**, chấm bài và trả lời học viên |
| Học viên | `student1@demo.example.test` | Học tiếp khóa đã ghi danh, ôn thẻ từ và xem nhận xét |
| Học viên | `student2@demo.example.test` | Thử ghi danh khóa miễn phí hoặc mua khóa học |

Trang đăng nhập có nút chọn nhanh **Học viên**, **Giảng viên** và **Quản trị** cho các tài khoản số 1. Để vào tài khoản số 2, nhập địa chỉ trong bảng và dùng cùng mật khẩu đã cấu hình khi tạo tài khoản.

---

### Dữ liệu được khởi tạo sẵn

- **Khóa học (Courses)**:
  - **Khóa học miễn phí (Free Course)**: Gồm các Module, Lesson đã xuất bản và bài thi cuối khóa (Final Assessment). Tài khoản `student1` đã được ghi danh sẵn vào khóa học này.
  - **Khóa học có phí (Paid Course)**: Dùng để thử nghiệm quy trình thanh toán và ghi danh trả phí.
  - **Khóa học bản nháp (Draft Course)**: Dùng cho giáo viên thử nghiệm chỉnh sửa và xuất bản.
- **Ngân hàng câu hỏi & Học liệu**:
  - Đầy đủ 4 kỹ năng: **Reading** (kèm bài đọc), **Listening** (kèm file audio mẫu), **Vocabulary**, **Grammar**.
  - Các bài thi Placement Test, Skill Assessment và Practice Test mẫu với đầy đủ cấu hình thời gian và trọng số điểm.

### Thử một lượt học và phản hồi

1. Đăng nhập bằng `student1@demo.example.test` và mở **Tiếng Anh tại quán cà phê**. Khóa có sáu bài, ba bài đầu đã hoàn thành. Dữ liệu mới khởi tạo có hoạt động vào năm ngày liên tiếp tính đến hôm qua.
2. Mở **Uống tại quán hay mang đi?** để ôn bộ **Từ vựng gọi món** và làm **Luyện gọi món theo 5 dạng câu hỏi**. Bài luyện có chọn đáp án, điền từ, ghép cặp, sắp xếp và nghe chép chính tả với tệp âm thanh lưu trong ứng dụng.
3. Trên **Hôm nay**, mở **Việc học** để xem hạn nộp bài **Viết lời nhắn đặt món** và phản hồi cho bài **Viết đoạn gọi món**. Bài **Lịch sự nhờ sửa món** đang chờ chấm. Phần hỏi đáp có một câu đã được trả lời và một câu đang chờ giảng viên.
4. Đăng nhập bằng `teacher2@demo.example.test` và mở **Việc cần xử lý** để chấm bài hoặc trả lời học viên. Trong **Bài tập viết** của khóa, chọn **Giao bài viết**, nhập đề, rồi **Xem trước**. Có thể giao ngay, lưu bản nháp đề hoặc hẹn giờ theo giờ Việt Nam.

Chạy lại lệnh seed giữ nguyên tiến độ, bài nộp và nội dung đã sửa. Tài khoản mẫu bắt đầu với các khóa miễn phí. Để thử khóa trả phí, dùng luồng thanh toán thử nghiệm.

---

## 5. Chạy kiểm thử (Testing)

Trên máy giới hạn RAM, chọn ca cần chạy và dùng `tests/low-memory.runsettings`. Các kiểm thử tích hợp dùng database tạm riêng trên SQL Server đã chỉ định qua `ELP_TEST_SQLSERVER`.

```bash
dotnet test tests/EnglishLearningPlatform.UnitTests -m:1 \
  --settings tests/low-memory.runsettings --filter 'FullyQualifiedName~GamificationPolicyTests'

dotnet test tests/EnglishLearningPlatform.IntegrationTests -m:1 \
  --settings tests/low-memory.runsettings --filter 'FullyQualifiedName~WritingHomeworkTests'
```
