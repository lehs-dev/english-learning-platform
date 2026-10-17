# Estudy V2 — Design System Master

**Ngày soạn**: 17/10/2026 · **Múi giờ**: Asia/Saigon  
**Phiên bản**: 2.0 (Friendly & Engaging / Calm Pastel EdTech)  
**Phạm vi**: Giao diện người dùng Estudy V2 (ASP.NET Core MVC, Razor, CSS thuần, JavaScript thuần).  
**Tài liệu tham chiếu**:
- Prototype đã duyệt: `artifacts/estudy-v2-prototype/` (Homepage & Student Dashboard)
- Design System cũ: `design-system/estudy/MASTER.md` (giữ nguyên để tham chiếu nghiệp vụ & dữ liệu)

---

## 1. Định Hướng Thiết Kế (Visual Direction)

Estudy V2 chuyển dịch từ phong cách enterprise tối giản sang **Friendly & Engaging / Calm Pastel EdTech** — trẻ trung, tích cực, truyền cảm hứng học tiếng Anh mỗi ngày nhưng đảm bảo độ thoáng đãng, sắc sảo và giảm tải thị giác tuyệt đối.

### Nguyên Tắc Cốt Lõi
1. **Khung Tranh Nổi (Floating Canvas Frame)**:
   - Giao diện đặt trên nền **Canvas Lavender** (`#EAE8FB`), nội dung nằm gọn trong một khung làm việc bo tròn lớn (`border-radius: 34px`) nền trắng `#FFFFFF` với bóng đổ dịu nhẹ `rgba(110, 95, 200, 0.08)`.
   - Loại bỏ hoàn toàn sự khô cứng của các dashboard quản trị truyền thống.
2. **Bảng Màu Pastel Thanh Nhã & Điểm Nhấn Sắc Nét (Calm Pastel Palette)**:
   - Nền pastel phân vùng nội dung nhẹ nhàng: Tím Periwinkle (`#DDD9FC`), Cam Đào (`#FED7AA`), Hồng Phấn (`#FFC5E3`), Bạc Hà (`#D1F5EA`).
   - Tương phản sắc nét với **Crisp Dark** (`#191A23`) trên các nút bấm hành động chính, chữ tiêu đề và trạng thái active.
3. **Thanh Điều Hướng Icon Rail Tinh Gọn (82px)**:
   - Thay thế sidebar cồng kềnh trên không gian học viên bằng thanh icon rail trực quan, hiện đại.
   - Các nút chức năng dạng vòng tròn mềm mại, icon 19px, trạng thái chọn chuyển màu nền đen `#191A23` tương phản cao.
4. **Nút Bấm & Bo Góc Hiện Đại (Firefox Friendly)**:
   - Nút hành động chính và các pill tag được bo góc cân đối, thân thiện trên mọi trình duyệt, không gây méo chữ.
5. **Thứ Bậc Thông Tin Rõ Ràng (Zero Clutter)**:
   - Phân chia bố cục 2 cột thoáng đãng (Cột chính 66% + Cột phụ 34%).
   - Tách biệt rõ ràng: Hero chào mừng & Học tiếp 1-click → Thẻ tiến độ 3 màu pastel → Bài viết / Gợi ý ôn tập → Lịch tuần & Khóa học sắp tới.

---

## 2. Bảng Màu & Design Tokens

Mọi màu sắc được định nghĩa thành biến CSS trong hệ thống `wwwroot/css/`:

| Token CSS | Giá trị Hex | Vai trò & Mục đích | Tỷ lệ tương phản sRGB |
| :--- | :--- | :--- | :--- |
| `--v2-canvas-bg` | `#EAE8FB` | Nền canvas dịu mắt bao bọc khung ứng dụng | Khung viền êm ái |
| `--v2-surface` | `#FFFFFF` | Nền khung canvas và các thẻ nổi | Chuẩn tương phản nền |
| `--v2-dark` | `#191A23` | Nút bấm chính, tiêu đề H1/H2, icon active | > 14:1 (Đạt chuẩn AAA) |
| `--v2-text-ink` | `#182238` | Văn bản chính, tiêu đề thẻ | > 12:1 (Đạt chuẩn AAA) |
| `--v2-text-secondary` | `#475569` | Mô tả phụ, tên giảng viên, nhãn metadata | > 5.8:1 (Đạt chuẩn AA) |
| `--v2-text-muted` | `#64748B` | Chú thích ngày tháng, đơn vị phụ | > 4.6:1 (Đạt chuẩn AA) |
| `--v2-pastel-periwinkle` | `#DDD9FC` | Banner hero chào mừng, thẻ tiến độ 1, ngày active | Điểm nhấn nhẹ nhàng |
| `--v2-pastel-peach` | `#FED7AA` | Thẻ tiến độ 2, tag danh mục bài học | Tươi tắn, ấm áp |
| `--v2-pastel-pink` | `#FFC5E3` | Thẻ tiến độ 3, tag danh mục từ vựng | Sinh động |
| `--v2-pastel-mint` | `#D1F5EA` | Tag danh mục ngữ pháp, chỉ số hoàn thành | Trực quan |
| `--v2-border-light` | `#F1F2F6` | Viền ngăn cách nhẹ giữa các khối | Tinh tế |
| `--v2-border-hover` | `#E2E4EE` | Viền khi hover thẻ khóa học | Tương tác nhẹ |

---

## 3. Typography & Font Chữ

- **Font chữ ưu tiên**: Plus Jakarta Sans / Be Vietnam Pro (tương thích 100% tiếng Việt có dấu, hiển thị sắc nét trên cả Windows, macOS, Linux, iOS, Android).
- **Fallback**: `-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif`.
- **Cỡ chữ & Trọng số**:
  - `Hero Title`: 32px - 38px, Font-weight: 800, Letter-spacing: -0.6px, Line-height: 1.25.
  - `Section Title`: 21px - 24px, Font-weight: 800, Letter-spacing: -0.3px.
  - `Card Title`: 14px - 16px, Font-weight: 700, Line-height: 1.35.
  - `Body Text`: 14px - 14.5px, Font-weight: 400, Line-height: 1.6.
  - `Metadata / Tag`: 11px - 12.5px, Font-weight: 600 - 700.

---

## 4. Hệ Thống Component Chuẩn Hóa

### A. Floating Canvas Frame
- Kích thước: Max-width 1400px, căn giữa màn hình với padding responsive (`24px 20px 40px` trên desktop, phẳng gọn không viền thừa trên mobile < 768px).
- Bo góc: `34px` (Desktop) / `0px` (Mobile full-width).
- Bóng đổ: `0 20px 60px rgba(110, 95, 200, 0.08)`.

### B. Nút Bấm (Button System)
- `btn-dark`: Nền `#191A23`, chữ `#FFFFFF`, bo góc `9999px` hoặc `8px`, hover đổi sang `#2D2E3B` kèm bóng mờ `rgba(25, 26, 35, 0.2)`.
- `btn-outline-dark`: Nền trong suốt, viền `1.5px solid #191A23`, chữ `#191A23`.
- `btn-dark-sm`: Phiên bản thu gọn cho thẻ khóa học và hành động phụ.

### C. Student Dashboard Layout
- **Thanh Icon Rail (82px)**:
  - Cột trái cố định chứa Logo biểu tượng trừu tượng V2.
  - Bộ icon tròn: Trang chủ học tập, Khóa học của tôi, Luyện tập, Đánh giá năng lực, Lịch sử giao dịch.
  - Dưới cùng: Cài đặt hồ sơ & Nút đăng xuất an toàn.
- **Topbar học viên**:
  - Ô tìm kiếm dạng viên thuốc nền `#F6F7FA`, icon kính lúp ẩn viền.
  - Chuông thông báo có chấm đỏ báo tin mới.
  - Nút Avatar học viên bo tròn với viền mượt.
- **Khối Học Tiếp 1-Click (Hero Promo Banner - `#DDD9FC`)**:
  - Hiển thị bài học đang học dở hoặc khóa học gần nhất.
  - Nút đen *"Tiếp tục học ngay"* đưa thẳng vào bài học (`Learning/Continue/{id}`).
  - Vector minh họa bạn học ngồi trên sách.
- **3 Thẻ Tiến Độ Màu Pastel (Your Progress)**:
  - Lavender (`#DDD9FC`), Peach (`#FED7AA`), Pink (`#FFC5E3`).
  - Thanh tiến độ tối giản (track `rgba(25, 26, 35, 0.14)`, fill `#191A23`).
  - Tỷ lệ phần trăm và số bài hoàn thành.
- **Khối Gợi Ý Ôn Tập & Luyện Tập (Top Articles / Recommendations)**:
  - 2 thẻ minh họa đẹp mắt tích hợp logic thực tế từ `SkillRecommendation` (<60%) và bài luyện tập gần đây.
- **Widget Lịch Tuần (Date Strip)**:
  - 7 ngày trong tuần với ngày hiện tại được đánh dấu bằng viên thuốc `#DDD9FC`.
- **Khóa Học Đề Xuất (Upcoming Courses)**:
  - Danh sách khóa học có tag màu pastel, học phí, số bài học và nút xem/đăng ký.

### D. Homepage Layout
- Header tối giản với logo V2, menu ngang, thanh tìm kiếm và nút *"Bắt đầu ngay"*.
- Hero Card màu Periwinkle `#DDD9FC` kết hợp minh họa học tập.
- Bộ lọc danh mục (Tất cả, Giao tiếp, Ngữ pháp, IELTS, TOEIC).
- Lưới 3 cột Course Cards sắc sảo.
- Lộ trình 4 bước học tập (4 thẻ pastel).
- Khối CTA đăng ký & Footer tinh gọn.

---

## 5. Ràng Buộc & Tính Tương Thích (Compatibility Rules)

1. **Phạm vi áp dụng an toàn**:
   - V2 áp dụng cho giao diện công cộng (Homepage, Courses) và Không gian Học viên (`_LayoutStudent.cshtml`, `Learning/Dashboard.cshtml`).
   - Giữ nguyên toàn bộ cấu trúc và chức năng của Giảng viên (`_LayoutTeacher.cshtml`) và Quản trị viên (`_LayoutAdmin.cshtml`), tránh ảnh hưởng đến các công cụ quản trị phức tạp.
2. **Bảo toàn 100% dữ liệu backend**:
   - Mọi form submission, antiforgery token, query parameter và route ASP.NET Core MVC được giữ nguyên vẹn.
   - Không sửa đổi database, migration, model hay domain entities.
3. **Phân định Real Data vs Mock Enhancement**:
   - Chỉ render các thẻ khi có dữ liệu thật từ ViewModel (`Model.Courses`, `continueCourse`, `Model.Results`, `Model.Recommendations`, `Model.PracticeResults`).
   - Các yếu tố trang trí giao diện (như ngày trong tuần) tự động lấy theo ngày giờ hệ thống thực (`DateTime.Today`).
