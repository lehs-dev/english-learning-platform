# Nâng dependency Pkcs — 14/10/2026

Theo yêu cầu người dùng sau khi nghiệm thu Tuần 6, Infrastructure thêm PackageReference trực tiếp `System.Security.Cryptography.Pkcs 10.0.13`. SqlClient trước đó kéo bản 9.0.11. Các project Web/UnitTests/IntegrationTests nhận bản mới qua dependency của Infrastructure.

Microsoft xác nhận 10.0.13 đã vá [CVE-2026-70162](https://github.com/dotnet/runtime/security/advisories/GHSA-2fmv-rv2c-59x7) và [CVE-2026-84008](https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx). Gói có target net10.0, phù hợp framework của solution.

Chọn 10.0.13 để application dùng DLL đã vá thay cho Pkcs 10.0.12 trong shared ASP.NET Core runtime. Probe chạy với deps/runtimeconfig của Web đã xác nhận DLL thực được nạp từ output ứng dụng có ProductVersion `10.0.13+4099f83977f4d29604a9aeb971fb0a7bc1d5f699`.

Kiểm chứng local trên SQL Server 2022:

- Restore/Release build: 0 warning, 0 error; NuGet audit toàn solution, gồm transitive packages, không báo package dễ tổn thương.
- Unit: 99/99 Pass; integration: 315/315 Pass, không fail/skip. 15 case focus nằm trong tổng integration.
- Browser UAT: 6 seeded + 10 Learning/Commerce Pass; đối chiếu dữ liệu SQL và cleanup container Pass.
- Chỉ thay cấu hình dependency; không thay business logic/schema. Bằng chứng Tuần 6 cũ giữ đúng candidate và warnings của lần chạy cũ.

Log/TRX/JSON/probe của lần kiểm chứng nằm trong `artifacts/dependency-pkcs-10.0.13`. Gate trước commit ghi HEAD nền `376ffe9`, workingTreeClean=false và source digest của thay đổi; không dùng SHA nền như SHA của source đã commit.
