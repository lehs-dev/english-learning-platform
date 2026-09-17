# English Learning Platform

Đồ án: **Xây dựng nền tảng học tập và đánh giá năng lực tiếng Anh trực tuyến trên nền web**.

## Mục tiêu MVP (Tuần 7)

Luồng bắt buộc phải chạy ổn định:

`Đăng ký/đăng nhập -> chọn khóa học -> học bài -> làm bài kiểm tra -> nộp bài -> chấm điểm -> xem kết quả -> cập nhật tiến độ`.

## Stack

- .NET 10 / ASP.NET Core MVC
- Entity Framework Core 10
- ASP.NET Core Identity
- SQL Server 2022
- xUnit
- Docker / Docker Compose
- GitHub Actions (CI + container delivery lên GHCR)

## Kiến trúc

Modular monolith theo 4 project chính:

- `Domain`: entity, enum, quy tắc lõi.
- `Application`: DTO, interface, service nghiệp vụ thuần.
- `Infrastructure`: EF Core, Identity, SQL Server.
- `Web`: ASP.NET Core MVC, middleware, controller, UI.

Test nằm trong `tests/`.

## Chạy local

Yêu cầu: .NET 10 SDK và SQL Server (local hoặc Docker).

```bash
cp .env.example .env
# chỉnh connection string/password nếu cần

dotnet restore EnglishLearningPlatform.sln
dotnet build EnglishLearningPlatform.sln --no-restore
```

Tạo migration đầu tiên (sau khi cài dotnet-ef):

```bash
dotnet tool install --global dotnet-ef --version 10.*
dotnet ef migrations add InitialCreate \
  --project src/EnglishLearningPlatform.Infrastructure \
  --startup-project src/EnglishLearningPlatform.Web \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project src/EnglishLearningPlatform.Infrastructure \
  --startup-project src/EnglishLearningPlatform.Web
```

Chạy web:

```bash
dotnet run --project src/EnglishLearningPlatform.Web
```

Health check: `GET /health`.

## Docker

Sau khi có migration:

```bash
docker compose up --build
```

Web: `http://localhost:8080`.

## Branch strategy

- `main`: release ổn định, chỉ merge qua PR.
- `develop`: nhánh tích hợp.
- `feature/<issue>-<short-name>`: feature.
- `fix/<issue>-<short-name>`: bugfix.

Không commit trực tiếp vào `main`. Mọi PR cần ít nhất một người review.

## Phân công lõi

- **Sơn:** kiến trúc, Identity/Security, integration, DevOps, performance, release.
- **Khánh:** Learning — Course/Lesson/Enrollment/Progress.
- **Hoàng:** Assessment — Question/Exam/Attempt/Scoring/Result.
- **Khang:** Admin/UI/QA/Data/Documentation/Reporting.

Đối với môn Công nghệ hiện đại, Sơn + Khánh + Hoàng + Dương phải đủ khả năng tự setup, trace request, sửa bug và giải thích toàn bộ hệ thống.

## Tài liệu

- ERD: `docs/architecture/erd.md`
- Kiến trúc: `docs/architecture/architecture.md`
- Quy ước Git/PR: `docs/project/git-workflow.md`
- GitHub Project/backlog: `docs/project/github-project.md`
- Kế hoạch vấn đáp: `docs/project/defense-readiness.md`

## CI/CD

- `.github/workflows/ci.yml`: restore -> build -> test.
- `.github/workflows/cd.yml`: build Docker image và push lên GitHub Container Registry khi push `main` hoặc tag `v*`.

CD hiện là **continuous delivery** tới GHCR. Deploy tới VPS/Azure sẽ bổ sung khi nhóm chốt môi trường đích.
