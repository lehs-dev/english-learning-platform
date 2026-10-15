# Estudy — Design System Master

Ngày soạn: 15/10/2026 · Múi giờ: Asia/Saigon  
Trạng thái: Phase 3–9 đã hoàn thành triển khai, kiểm tra và bàn giao toàn bộ UI.  
Phạm vi: ASP.NET Core MVC / Razor, CSS hiện tại và JavaScript thuần.  
Nguồn thống nhất: file MASTER.md này. Token, component chung, bốn layout và các nhóm trang đã áp dụng; chưa tạo page override.

## 1. Hướng thiết kế

Giáo dục hiện đại, nền sáng, xanh Estudy, điểm nhấn cam ấm; gọn như một công cụ học tập nhưng có cá tính và dễ tiếp cận.

- Giữ màu xanh #176bff làm dấu nhận diện xuyên suốt bốn vai trò.
- Bố cục có nhịp, thứ bậc chữ rõ, nội dung thực và thao tác học tập đứng trước trang trí.
- Dấu hiệu nhận diện: hình ảnh khóa học phẳng từ chữ/kỹ năng, dải tiến độ rõ, trạng thái học có nhãn tiếng Việt, chi tiết cam ấm dùng tiết chế.
- Trang chủ dùng bố cục rộng và giàu hình ảnh; bài học ưu tiên đọc; trang thi giảm trang trí; công cụ quản lý ưu tiên quét thông tin.
- Một lớp surface/card. Các phần nằm bên trong chia bằng khoảng cách, dòng kẻ hoặc tiêu đề; không tiếp tục bọc card trong card.
- Không dùng nền tối toàn trang, gradient tím/hồng, gradient chữ, kính mờ, 3D mềm hoặc font hoạt hình.

### Đối chiếu ảnh tham khảo

Bốn ảnh được người dùng đính kèm trực tiếp trong hội thoại. Thư mục ui-reference chưa tồn tại tại thời điểm Phase 1.

| Ảnh | Điều tiếp thu | Áp dụng cho Estudy |
| --- | --- | --- |
| Study4 — trang chủ | Banner CTA gọn, khóa học có vùng hình ảnh, giá dễ quét | Hero có một hành động chính; danh sách khóa có hình minh họa định danh, metadata và giá thật |
| Study4 — thư viện đề thi | Tìm kiếm/lọc gần kết quả, nhịp lưới và mật độ thông tin | Catalog và danh sách bài đánh giá có khu tìm/lọc, kết quả và phân trang rõ ràng |
| Study4 — chi tiết khóa | Đề cương thành các dòng bài học, thông tin phụ ở sidebar | Cột đề cương + sidebar giá/giảng viên; trạng thái hoàn thành chỉ lấy từ model được cấp |
| Moon — bài học | Nội dung chính lớn, navigator và thông tin phụ | Một vùng đọc chính + vùng điều hướng gọn; mobile chỉ giữ nội dung và hành động cần thiết |

Không sao chép logo, thumbnail, người dạy, nội dung, số liệu hoặc cấu trúc quảng cáo/chat của các website tham khảo.

### Kết quả generator và điều chỉnh

Đã chạy ui-ux-pro-max trong image Python 3.13 có sẵn của Docker; không cài Python lên máy host.

1. Truy vấn gốc: online English learning platform for Vietnamese students.
2. Kết quả đầu: Claymorphism, teal, Baloo 2 + Comic Neue; không phù hợp định hướng người dùng.
3. Chạy lại: adult language learning minimal professional light blue, variance 2, density 5.
4. Giữ khung Minimalism & Swiss Style: rõ ràng, thoáng, lưới nhất quán, tải nhanh.
5. Thay màu indigo và font thiếu nhi bằng bảng màu/typography bên dưới theo brief.
6. Loại khối testimonials vì chưa có lời chứng thực thật; không dùng số liệu niềm tin giả.
7. Các quyết định bên dưới thay thế hoàn toàn phần đề xuất tự động không phù hợp. Không còn một MASTER cạnh tranh.

## 2. Màu và token

Sáu token hiện có giữ nguyên giá trị. Giá trị màu mới chỉ được đưa vào site.css dưới dạng token; CSS của từng cụm dùng var(--...).

| Token | Giá trị | Mục đích |
| --- | --- | --- |
| --primary | #176bff | Hành động chính, liên kết, trạng thái được chọn, nhận diện |
| --primary-dark | #0751cf | Hover/focus của hành động chính, liên kết trên nền nhạt |
| --ink | #101d48 | Tiêu đề và nội dung chính |
| --muted | #7182a4 | Token kế thừa; trang trí và chữ lớn đủ tương phản; không dùng cho metadata nhỏ trên nền trắng |
| --border | #e4ecf7 | Dòng kẻ và viền surface mang tính trang trí |
| --page | #f5f9ff | Nền trang sáng |
| --surface | #ffffff | Surface nội dung, header, sidebar |
| --text-secondary | #52627c | Metadata, mô tả phụ, nhãn và chú thích 14px |
| --border-strong | #8393ab | Ranh giới input/control cần nhìn rõ |
| --primary-soft | #edf4ff | Mục được chọn và thông tin phụ màu xanh |
| --accent | #bd4c12 | CTA nổi bật trên trang chủ, điểm nhấn động lực có dữ liệu thật |
| --accent-dark | #a63c0b | Hover của CTA cam |
| --accent-soft | #fff3e8 | Nền nhấn ấm |
| --success | #16724b | Hoàn thành, kết quả đạt, trạng thái thành công |
| --success-soft | #edf8f0 | Nền thông báo thành công |
| --danger | #b42318 | Lỗi, thao tác phá hủy, hạn chế cuối giờ cần chú ý |
| --danger-soft | #fff0ed | Nền thông báo lỗi |
| --warning | #8c4a0a | Chú ý, nháp, cảnh báo sandbox |
| --warning-soft | #fff6dd | Nền cảnh báo |
| --focus-ring | #176bff | Focus bàn phím, kết hợp khoảng cách trắng |

Đã tính tương phản sRGB cho các cặp dùng phổ biến:

| Cặp | Tỷ lệ |
| --- | --- |
| Trắng / primary | 4.58:1 |
| Trắng / primary-dark | 6.80:1 |
| Trắng / accent | 4.99:1 |
| text-secondary / trắng | 6.18:1 |
| text-secondary / page | 5.85:1 |
| Trắng / success | 5.93:1 |
| Trắng / danger | 6.57:1 |
| border-strong / trắng | 3.12:1 |

--muted trên trắng chỉ đạt 3.87:1: giữ token theo brief nhưng chuyển chữ nội dung nhỏ sang --text-secondary. Mọi nhãn trạng thái phải có chữ hoặc ký hiệu rõ, không chỉ đổi màu.

## 3. Typography

Font chính: Be Vietnam Pro, một family cho cả tiêu đề và thân bài.  
Fallback: "Segoe UI", system-ui, Arial, sans-serif.  
Trọng lượng sử dụng: 400, 500, 600, 700. Không dùng font mảnh cho nội dung.

Be Vietnam Pro có subset Vietnamese và giấy phép OFL trong [metadata chính thức của Google Fonts](https://github.com/google/fonts/blob/main/ofl/bevietnampro/METADATA.pb). Khi triển khai chỉ tải một family với display=swap, fallback vẫn đọc được khi mạng lỗi; không chặn việc học hoặc đồng hồ thi để đợi font.

| Token / vai trò | Mobile | Desktop | Line-height / weight |
| --- | --- | --- | --- |
| --text-hero | 32px | 52px | 1.2 / 700 |
| --text-h1 | 28px | 36px | 1.25 / 700 |
| --text-h2 | 24px | 28px | 1.35 / 700 |
| --text-h3 | 18px | 20px | 1.4 / 600 |
| --text-body | 16px | 16px | 1.65 / 400 |
| --text-reading | 17px | 18px | 1.8 / 400 |
| --text-small | 14px | 14px | 1.5 / 400–500 |
| Nhãn và button | 14–16px | 14–16px | 1.4 / 600 |

- Nội dung đọc, phụ đề, metadata và label không nhỏ hơn 14px.
- Đoạn đọc dài khoảng 65–72 ký tự mỗi dòng; không giãn chữ nội dung.
- Tiêu đề tiếng Việt dùng letter-spacing từ 0 đến -0.02em, không nén dấu.
- Không lạm dụng eyebrow in hoa. Ưu tiên tiêu đề tiếng Việt rõ ràng.
- Điểm số, giá, đồng hồ dùng tabular-nums, không đổi sang một font trang trí.
- Từ chuyên môn trong nội dung bài học được giữ nguyên; nhãn ứng dụng dùng tiếng Việt. Các ngoại lệ kỹ thuật như Quiz, CEFR, ID chỉ xuất hiện khi đúng ngữ cảnh.

## 4. Khoảng cách, bo góc và độ nổi

| Nhóm | Token / giá trị | Sử dụng |
| --- | --- | --- |
| Spacing | --space-1: 4px; --space-2: 8px; --space-3: 12px; --space-4: 16px | Khoảng cách icon, nhãn, các dòng liên quan |
| Spacing | --space-5: 20px; --space-6: 24px; --space-8: 32px | Padding surface, khoảng cách cột và nhóm form |
| Spacing | --space-12: 48px; --space-16: 64px | Khoảng cách section trang chủ |
| Radius | --radius-sm: 6px | Tag nhỏ, nút navigator |
| Radius | --radius-control: 10px | Button, input, select, textarea |
| Radius | --radius-card: 14px | Card khóa học và surface độc lập |
| Radius | --radius-hero: 20px | Hero trang chủ |
| Radius | --radius-pill: 999px | Badge ngắn và avatar chữ cái |
| Shadow | --shadow-sm: 0 1px 3px rgb(16 29 72 / 4%) | Card có viền nhẹ |
| Shadow | --shadow-md: 0 6px 20px rgb(16 29 72 / 7%) | Hover của card tương tác, menu |
| Shadow | --shadow-lg: 0 18px 48px rgb(16 29 72 / 14%) | Dialog và dropdown nổi |
| Layout | --content-max: 1280px; --reading-max: 72ch | Giới hạn nội dung và vùng đọc |
| Layout | --sidebar-width: 240px; --aside-width: 304px | Khu quản lý và sidebar trang khóa |

Surface thường dùng viền nhẹ, ít bóng. Không có bóng đậm quanh mọi khối. Hero giữ nền phẳng, điểm nhấn bằng bố cục, chữ và hình minh họa.

## 5. Thành phần dùng chung

| Thành phần | Quy tắc |
| --- | --- |
| Button chính | Xanh, chữ trắng; cao tối thiểu 44px; một hành động chính mỗi khu vực |
| Button nổi bật trang chủ | Cam ấm; cùng kích thước/radius với button chính; không dùng trên trang thi |
| Button phụ | Nền trắng, chữ primary-dark, viền rõ; không cạnh tranh với CTA chính |
| Button ghost / liên kết phụ | Vùng bấm đủ lớn; nhãn rõ; không biến mọi liên kết thành button |
| Button nguy hiểm | Nhãn và màu danger; giữ xác nhận/điều kiện hiện có |
| Input/select/textarea | Label hiển thị, chữ 16px, viền border-strong, lỗi gần trường; không dùng placeholder thay label |
| Card khóa học | Một khối độc lập: vùng hình ảnh, tiêu đề, metadata thật, giá, CTA; không nhét thêm card con |
| Badge | Trạng thái ngắn bằng tiếng Việt, nền nhạt + chữ đậm; không dùng quá nhiều tag |
| Notice | Giữ TempData và validation; có tiêu đề/ngữ cảnh, màu trạng thái và role thích hợp |
| Table | Caption giữ nguyên, header rõ, hàng đọc thoáng; mobile giữ data-label theo từng nhóm |
| Pagination | GET/filter được bảo toàn; mục hiện tại có aria-current; mục trước/sau có tên dễ hiểu |
| Breadcrumb | Có nhãn điều hướng, mục hiện tại rõ; dùng đúng route hiện có |
| Back-link | Cùng một kiểu button secondary/back-link; icon arrow-right xoay hướng phù hợp + nhãn tiếng Việt |
| Tiến độ | Có nhãn số bài hoàn thành/tổng, giá trị từ backend; null không trình bày thành 0% hoặc 100% |
| Hình/ảnh khóa học | Định danh bằng bố cục chữ/kỹ năng và SVG hiện có trên nền phẳng; không giả là ảnh khóa do giáo viên cung cấp |
| Avatar | Chữ cái tên thật hoặc biểu tượng user khi không có ảnh; không ảnh stock giả giáo viên |
| Dialog | Dùng dialog hiện có, focus được quản lý, Escape đóng theo hành vi hiện tại, không kéo dài ra ngoài viewport |

- Icon dùng sprite ~/images/icons.svg; không cài thêm thư viện.
- Chỉ dùng symbol đang có, kết hợp nhãn chữ. Resource không có loại trong model thì không tự gán icon video/âm thanh.
- Biểu tượng eye dành cho xem trước, check-circle cho trạng thái/câu hỏi, graduation cho khóa/học tập; search chỉ cho tìm kiếm.
- Ngoại lệ minh họa cover: icon nằm trong vùng aria-hidden, không phải control; eye gợi việc đọc và search gợi khám phá từ vựng. Không dùng các biểu tượng trang trí này để thay nhãn của hành động.
- Logo giữ Estudy + sprite graduation. Favicon có thể nhúng SVG data URI từ cùng hình logo trong layout để không cần thêm file ngoài phạm vi đã chốt.
- Chuyển màu/viền/bóng trong 140–180ms; không animation chạy liên tục, không dịch chuyển card khi hover. Tôn trọng reduced-motion.

## 6. Responsive

| Viewport | Quy tắc chính |
| --- | --- |
| 375px | Gutter 16px; một cột; filter gấp gọn; đề cương/navigator mở theo yêu cầu; button/đáp án dễ bấm; không tràn ngang |
| 768px | Gutter 24px; hai cột card nếu đủ chỗ; bài học/thi vẫn ưu tiên một vùng nội dung chính; side navigation là drawer |
| 1024px | Gutter 24px; hai cột kết quả catalog cùng sidebar lọc 232px; header/sidebar theo vai trò dùng menu mobile |
| 1440px | Gutter 32px; max-width 1280px; catalog có sidebar lọc 232px và ba cột kết quả; khóa/bài học có cột phụ khi hữu ích |

- Không đặt cột thứ ba nếu làm vùng đọc nhỏ hơn 560px.
- Sidebar Teacher/Admin rộng 240px ở desktop, thu về drawer trên tablet/mobile.
- Drawer có backdrop, Escape, trả focus về nút mở; giữ keyboard behavior của site.js.
- Header 72px desktop, 64px mobile. Search có thể thành một dòng riêng thay vì ép cạnh logo.
- Trang thi mobile có thanh hành động gọn không che câu hỏi; dùng khoảng trống bù cho vùng sticky.
- Bảng quá rộng dùng responsive data-label; chỉ cuộn ngang vùng bảng khi loại dữ liệu thực sự cần hai chiều.
- Kiểm tra nội dung tiếng Việt dài, tên khóa dài, 200% zoom và viewport thấp.

## 7. Pattern theo nhóm trang

### Trang chủ

Mode: thuyết phục người dùng khám phá khóa thật.

Hero có một CTA chính và một CTA phụ, hình minh họa học tập phẳng; tiếp theo là khóa học hiện có, nhóm kỹ năng, giải thích luồng học và CTA bài đánh giá. Ở desktop dùng ba card/nhóm; mobile dùng một cột hoặc danh sách cuộn tay có link hoạt động. Không tự chạy carousel.

Các khối chứng minh chất lượng phải dùng dữ liệu có thật. Catalog model không có tổng học viên hay review nên không tạo số liệu tin cậy, rating sao hoặc testimonials. Không hứa chứng nhận CEFR, chấm Nói/Viết hoặc bài kiểm tra đầu vào miễn phí công khai khi hệ thống chưa cung cấp đúng luồng đó. CTA đánh giá dùng Assessment/Index và tôn trọng đăng nhập/quyền hiện tại.

### Catalog khóa học và danh sách bài đánh giá

Mode: tìm đúng nội dung và mở chi tiết.

Catalog: tiêu đề + tổng kết quả thật, search/filter gần kết quả, sidebar lọc 232px khi rộng hơn 760px và filter gấp gọn từ 760px trở xuống. Card có vùng minh họa ưu tiên 16:9, tối thiểu 180px trên desktop/170px trên mobile để giữ chữ và nhãn dễ đọc; tên khóa, giáo viên, trình độ, kỹ năng, số bài/module và giá hiện tại. Giữ mọi filter và phân trang hiện có; không thêm sort/filter mà controller chưa hỗ trợ.

Danh sách đánh giá: dùng metadata thực của từng bài, thời lượng/số câu/lượt và trạng thái khả dụng theo model; chỉ cung cấp tìm/lọc nếu route/model hiện có thực sự hỗ trợ. Không nhập các danh mục TOEIC/IELTS từ ảnh tham khảo khi dữ liệu không có.

### Chi tiết khóa học

Mode: hiểu nội dung, giá và cách tham gia.

Cột chính gồm giới thiệu/mục tiêu và đề cương theo module; sidebar gồm giá, trạng thái có quyền học, tên giáo viên và hành động tham gia/mua/tiếp tục đúng điều kiện. Teacher chỉ có avatar chữ cái và tên; không tự tạo hồ sơ/badge chứng thực.

Dòng bài học dùng chỉ báo bài học chung khi LessonOutline chưa có resource type. Public detail không có dữ liệu tiến độ và không có enrollment count thật; không hiển thị số 0 mặc định như tổng học viên. Chỉ trang Learning có IsCompleted/Progress mới hiển thị trạng thái hoàn thành. Không tạo bookmark hoặc badge miễn phí từng bài.

### Dashboard và khu học tập

Mode: tiếp tục đúng bài và hiểu tiến độ.

Dashboard đặt hành động tiếp tục học trước, sau đó là khóa đang học, kết quả thực và gợi ý ôn tập có sẵn. Bỏ mốc streak 3 ngày mặc định hiện tại; không xây XP, thống kê tuần hoặc streak từ lịch sử thiếu dữ liệu.

Trang Course/Module dùng đề cương thành dòng, chỉ báo hoàn thành, progress tổng và bài đánh giá cuối khóa theo quyền thật. Trang Lesson có breadcrumb, vùng đọc, tài nguyên text/link/audio/video dựa trên ResourceContent.Type, điều hướng bài trước/sau và nút hoàn thành rõ.

Vùng nội dung chính không bọc thêm card khi tài nguyên đã là block độc lập. Mobile đưa tài liệu và hành động học lên trước thông tin phụ. Mốc hoàn thành khóa dùng CompletedAt thật và không diễn giải thành chứng chỉ.

### Practice và Assessment

Mode: hoàn thành bài, giữ tập trung và tin cậy trạng thái đã lưu.

Màn hình thi có header rút gọn, tên bài, đồng hồ vừa đủ, tiến độ trả lời, vùng câu hỏi/đáp án và navigator. Đáp án có cả input thật và vùng nhãn đủ lớn. Navigator mobile gấp gọn; không thêm cạnh tranh thị giác quanh thời gian còn lại.

Giữ nguyên assessment.js, deadline phía server, auto-save, input name/ID/data-attribute và noscript fallback. Practice thêm navigator và đếm câu trả lời bằng JS thuần; không thêm cơ chế lưu nháp mà BE chưa có.

Assessment Result có tổng điểm, đạt/chưa đạt khi có PassingScore, breakdown kỹ năng và lịch sử thật; không lộ đáp án đúng. Practice Result có điểm, số câu đúng và đáp án/giải thích hiện có; không tự tạo breakdown kỹ năng vì PracticeQuestionFeedback chưa chứa kỹ năng.

### Không gian giảng dạy và ngân hàng câu hỏi

Mode: quản lý đúng nội dung sở hữu với ít thao tác hơn.

Dùng shell sáng, sidebar rõ nhóm, trang danh sách có thống kê thật và hành động chính; trang nội dung chuyển cây module/bài/tài nguyên thành các hàng có nhóm thay vì nhiều lớp panel. Trang form có section và label nhất quán, lỗi rõ, required có cả chữ/ngữ nghĩa.

Việt hóa nhãn giao diện: Không gian giảng dạy, Ngân hàng câu hỏi, Đã xuất bản, Bản nháp, Bài học. Giữ tiếng Anh trong nội dung học và thuật ngữ kỹ thuật cần thiết. TeacherPractices chỉ có màn hình tạo nháp hiện hữu; không thêm edit/publish Practice.

### Quản trị

Mode: tra cứu và thực hiện thao tác tài khoản chính xác.

Shell dùng cùng nền/type/token với Teacher; sidebar theo quyền Admin. Bảng, filter và dialog chú trọng thứ bậc, khoảng cách và trạng thái. Giữ data-label, caption, CSRF, thông báo backend và giờ Việt Nam UTC+7. Giao dịch không có nút hoàn tiền/chức năng mới.

### Tài khoản, hồ sơ và checkout

Tài khoản: form gọn; một khối form chính, thông tin phụ nhẹ, demo account switch giữ nguyên. Hồ sơ: tên, email/vai trò/trạng thái đúng quyền, hành động lưu rõ ràng.

Checkout: tóm tắt khóa/giá, trạng thái đơn và hành động có sẵn. Hiển thị “Thanh toán mô phỏng — không thu tiền thật” khi IsSandbox/SandboxEnabled cho biết đúng chế độ. Không form nhập thẻ/ngân hàng; gateway tắt có hướng dẫn hợp lệ. Không áp nhãn sandbox cho hosted order nếu sau này hosted được cấu hình thật.

## 8. Hợp đồng dữ liệu và giới hạn

| Chi tiết mong muốn từ ảnh tham khảo | Dữ liệu hiện tại | Quyết định thiết kế |
| --- | --- | --- |
| Rating sao, review, giá cũ, giảm giá | Không có trong CourseSummary | Không hiển thị và không tạo số mẫu |
| Tổng học viên trên catalog/public detail | Catalog không có; PublicDetail trả EnrollmentCount=0 cố định | Chỉ dùng enrollment count trong khu quản lý Teacher được cấp dữ liệu thật |
| Thumbnail/ảnh giáo viên | Model chưa có URL ảnh | Minh họa trang trí + avatar chữ cái |
| Hồ sơ giáo viên/badge xác thực | Chỉ có tên Teacher | Chỉ hiển thị tên và thông tin thật đã có |
| Bookmark bài học | Chưa có route/model lưu | Không dựng nút bookmark |
| FREE / tiến độ % mỗi dòng | Không có free flag riêng cho Lesson; completion là bool ở Learning | Miễn phí theo Course.IsPaid; dùng nhãn hoàn thành/chưa hoàn thành đúng model |
| Streak, XP, tiến độ tuần | Không có thống kê đầy đủ; view hiện có fallback streak=3 | Loại fallback giả; dùng course progress/kết quả/gợi ý thật |
| Breakdown kỹ năng của Practice Result | Feedback không chứa Skill | Điểm tổng + đúng/sai từng câu; giữ breakdown kỹ năng cho Assessment Result |
| Thi đầu vào miễn phí, chứng nhận CEFR, chấm Nói/Viết | Không được xác nhận bởi luồng hiện tại | Không viết lời hứa chức năng này |
| Dữ liệu trống | Có thể xuất hiện thật | Empty state có giải thích/hành động hợp lệ; không bịa khóa hoặc kết quả để lấp chỗ trống |

Không thêm truy vấn database/service mới trong Razor để lấp khoảng trống dữ liệu. Chỉ trình bày dữ liệu controller/model hiện tại và trạng thái UI cục bộ được phép.

## 9. Ba quyết định dead-UI đã chốt

1. Header Guest/Học viên: form GET tới Courses/Index; input name=Search đúng CatalogQuery.Search; bỏ disabled; nhãn “Tìm khóa học”.
2. Xóa chuông thông báo chưa có backend. Giữ thông báo TempData và validation.
3. Menu Guest: giữ Khóa học; Đề thi tới Assessment/Index; xóa Đánh giá năng lực trùng, Giới thiệu và Dịch từ.

Đây là ngoại lệ điều hướng markup đã được người dùng chỉ định; không thay route/controller/model.

## 10. Trạng thái và khả năng tiếp cận

- Giữ skip-link, focus-visible, label, aria-label, caption, TempData và noscript.
- Đích bấm chính tối thiểu 44×44px; control liên quan không dính nhau.
- Focus dùng outline rõ + khoảng cách trắng; không outline:none thay thế bằng bóng yếu.
- Không dấu thông tin quan trọng chỉ trong hover; thông báo lỗi đọc được bằng bàn phím và screen reader.
- Empty state và unavailable phải giải thích đúng thực tế và có hành động hiện hữu.
- Không bỏ data-attribute, input name/id, field binding, antiforgery hoặc trạng thái role/ownership.
- Resource ngoài có thể lỗi mạng: nội dung và trạng thái còn lại vẫn sử dụng được.
- Nội dung động HTML vẫn do Razor encode theo luồng hiện có.
- Giữ UTC+7 ở mọi ngày/giờ người dùng.
- Không dùng auto-rotation; không thêm animation quanh timer; reduced-motion được tôn trọng.

## 11. Kế hoạch áp dụng và điểm dừng

Phase 1 chỉ chốt tài liệu này. Không sửa site.css, layout, controller, model hoặc JS ứng dụng trong Phase 1.

Sau khi người dùng review:
- Phase 2: token + bốn layout + base components + ba quyết định dead-UI; build và ảnh desktop/mobile; dừng review.
- Phase 3–8: mỗi nhóm trang theo đúng thứ tự và điểm dừng trong brief.
- Phase 9: audit/critique và xử lý findings trong phạm vi frontend.
- Đối chiếu diff phần triển khai chỉ gồm .cshtml/.css/.js. MASTER.md là tài liệu thiết kế Phase 1, được báo riêng, không tính là thay đổi backend.
- Không tuyên bố đã build/chụp UI mới trong Phase 1. Bộ ảnh hiện trạng vẫn ở artifacts/ui-current-2026-10-15.

## 12. Nguồn đối chiếu

- Brief người dùng và bốn ảnh tham khảo trong hội thoại.
- .agents/skills/ui-ux-pro-max/SKILL.md; generator Python, hai truy vấn đã ghi ở mục 1.
- .agents/skills/impeccable/SKILL.md: brief người dùng và product truth chi phối thiết kế.
- src/EnglishLearningPlatform.Application/Learning/CourseModels.cs.
- src/EnglishLearningPlatform.Application/Learning/StudentDashboardModels.cs.
- src/EnglishLearningPlatform.Application/Practice/PracticeModels.cs.
- src/EnglishLearningPlatform.Application/Assessment/AssessmentTakingModels.cs.
- src/EnglishLearningPlatform.Application/Commerce/CheckoutModels.cs.
- src/EnglishLearningPlatform.Infrastructure/Learning/CourseService.cs: public enrollment count và dữ liệu theo ownership.
- src/EnglishLearningPlatform.Web/Views/Learning/Dashboard.cshtml: fallback streak cần loại trong Phase 5.
- src/EnglishLearningPlatform.Web/wwwroot/css/site.css: sáu token thương hiệu kế thừa.
- artifacts/ui-current-2026-10-15/index.html: 26 ảnh hiện trạng, chưa phải redesign.


## 13. Bổ sung đã được người dùng chốt trước Phase 2

- Tập hợp bốn ảnh tham khảo dưới ui-reference/ khi có file ảnh gốc. Người dùng đã xác nhận hiện chỉ có ảnh trong hội thoại; danh mục tại ui-reference/README.md ghi rõ tình trạng, không thay bằng ảnh dựng lại.
- Phase 3 phải trình mẫu cover khóa học trước khi áp dụng: mỗi kỹ năng một tint màu, icon từ sprite và pattern nhẹ có chủ đích. Cover cần trông như nội dung đã biên tập, không phải ô giữ chỗ.
- Dừng để người dùng duyệt mẫu cover bằng mắt rồi mới triển khai cover trên danh sách khóa. Mẫu không có rating, giảm giá hoặc số học viên giả.

## 14. Cập nhật phạm vi ngày 15/10/2026

Người dùng yêu cầu tiếp tục đến hết, không dừng để review giữa các phase. Yêu cầu mới này thay thế các điểm dừng Phase 3–9 và bước chờ duyệt mẫu cover ở mục 13. Vẫn giữ giới hạn chỉ frontend và không commit/push.

- Đã áp dụng cover chữ với bốn tint theo kỹ năng, icon sprite và pattern nhẹ. Bộ ảnh `artifacts/redesign-final/screenshots/cover-studies.png` ghi mẫu bốn biến thể; không phải danh sách khóa học hoặc dữ liệu minh họa trong ứng dụng.
- Trang chủ dùng dữ liệu khóa học thật từ model, không thêm khóa giả khi danh mục rỗng, không tuyên bố chứng nhận hoặc năng lực chấm Nói/Viết chưa có.
- Catalog có sidebar lọc 232px trên desktop và disclosure gấp gọn trên mobile; giữ toàn bộ tên trường, giá trị enum, query và phân trang.
- Chi tiết khóa hiển thị tên/mô tả trước hành động tham gia trên mobile. Đề cương theo dòng chương/bài và sidebar chỉ có dữ liệu thật.
- Dashboard ưu tiên tiếp tục học, sau đó thống kê/khóa đã tham gia, kết quả và gợi ý. Đã bỏ streak mặc định giả và truy vấn hồ sơ riêng trong view dashboard.
- Bài học dùng một vùng đọc và mục lục tài nguyên; mobile xếp thông tin phụ sau bài. Tiến độ và hoàn thành vẫn dùng thao tác backend hiện hữu.
- Practice có navigator và đếm câu đã trả lời bằng JS cục bộ; không tạo chức năng lưu nháp. Assessment giữ nguyên đồng hồ, tự lưu, xác nhận nộp và noscript trong luồng hiện hữu.
- Teacher có thống kê thật, nhóm chương/bài/tài nguyên; chỉ tạo nháp Practice. Question bank giữ quyền sở hữu, khóa nội dung chấm và JS thêm lựa chọn hiện tại.
- Admin giữ dữ liệu bảng, filter, dialog và nhật ký; nhãn giao diện Việt hóa. Account giữ ba nút demo cùng cơ chế điền tài khoản/mật khẩu.
- Checkout hiển thị rõ mô phỏng khi flag tương ứng bật, và thông báo không khả dụng khi tắt; không thêm form thẻ/ngân hàng. Cấu hình mặc định của repo không thay đổi.
- Bộ ảnh và kiểm tra ở `artifacts/redesign-final/`; tài liệu audit ghi từng finding và xử lý, gồm false positive của đường gạch chân tab đang chọn.
