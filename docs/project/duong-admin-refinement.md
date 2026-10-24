# Hoàn thiện thao tác Admin

## Doanh thu VND

- Giữ bộ lọc ngày tùy chọn; thêm Tuần này, Tháng này, Năm này và Toàn bộ lịch sử.
- Mở `/Admin/Revenue` không có bộ lọc hoặc bấm Đặt lại mặc định chọn Tháng này theo UTC+7, từ đầu đến hết tháng. Khi URL có ngày từ/đến mà không có preset, vẫn dùng khoảng Tùy chọn do người dùng cung cấp.
- Tính lịch theo UTC+7. Tuần từ thứ Hai đến Chủ nhật; tháng/năm dùng đầy đủ kỳ lịch.
- Chọn nhanh tự điền ngày. Sửa ngày chuyển về Tùy chọn. Server vẫn tự xác định khoảng preset.
- Tổng doanh thu ghi rõ tên kỳ và ngày bắt đầu/kết thúc. Số đơn theo ngày tạo có nhãn riêng.
- Biểu đồ đường SVG theo ngày/tháng, có giá trị 0 cho mốc không có doanh thu trong khoảng. Chạm/di chuột vào điểm xem số tiền và số đơn.
- Chỉ lấp khoảng trống trên kết quả đã tổng hợp SQL, tối đa 3.660 mốc; khoảng rộng hơn dùng các mốc tổng hợp có doanh thu. Bảng vẫn phân trang các kỳ có doanh thu và giữ bộ lọc.
- Không thêm thư viện hoặc CDN. Số tiền trong bảng/tổng vẫn dùng decimal; SVG chỉ biểu diễn hình học.

## Người dùng

- Bỏ nút Tạo giảng viên ở Dashboard. Mục Người dùng có nút Tạo người dùng.
- Form chọn Học viên (Student), Giảng viên (Teacher), Quản trị viên (Admin).
- Giao diện dùng cùng `account.css` với Login: khung hai cột, phần giới thiệu tím nhạt và form bên phải. Trên điện thoại hiển thị form một cột, có hiện/ẩn mật khẩu bằng handler dùng chung. Không tạo modal hoặc chuyển sang luồng đăng ký công khai.
- Chỉ Admin đang hoạt động có quyền tạo. Server giới hạn đúng ba role, dùng Identity hash mật khẩu, gán một role và ghi audit CreateUser trong transaction có khóa quản trị hiện tại.
- POST có antiforgery; không điền lại mật khẩu khi lỗi. Từ chối role lạ và email trùng.
- Giữ endpoint CreateTeacher cũ để tương thích; giao diện mới dùng CreateUser.

## Khóa học

- Lọc ngày tạo từ/đến kết hợp tìm kiếm và trạng thái. Múi giờ Việt Nam, bao gồm hết ngày kết thúc.
- Mới nhất: CreatedAtUtc giảm dần. Phổ biến: lượt đăng ký của khóa giảm dần.
- Giảng viên nổi bật: tổng lượt đăng ký các khóa của giảng viên giảm dần, sau đó lượt đăng ký khóa, thời gian tạo và Id để phân trang ổn định.
- Các lượt đăng ký tính toàn lịch sử, khớp thống kê Dashboard. Bộ lọc ngày áp dụng ngày tạo khóa; không giới hạn ngày đăng ký. Tiêu chí ghi rõ trên giao diện.
- Count, lọc ngày, xếp hạng và phân trang đều thực hiện ở SQL bằng EF Core. Không tải toàn bộ dữ liệu rồi xếp hạng trong C#.
- Bỏ nút Về tổng quan thừa; dùng điều hướng Admin chung.
- Phân biệt mục tra cứu toàn hệ thống với liên kết Xem danh mục công khai. Khi Admin mở `/Courses`, tiêu đề và mô tả nêu rõ đây là danh mục dành cho người học, chỉ có khóa Published với chủ sở hữu không Disabled theo truy vấn hiện có. Guest/Student giữ lời giới thiệu học tập ban đầu.
- Rút phần giải thích bộ lọc thành một dòng theo lựa chọn sắp xếp. Đổi nhãn TeacherPopular thành Lượt đăng ký của giảng viên, không gợi ý đây là đánh giá chất lượng giảng dạy.
- Thẻ khóa học dùng tên khóa thực tế thay chữ Grammar/Reading trên ảnh bìa; nếu nhiều kỹ năng thì ghi Nhiều kỹ năng. Giữ tên dạng link ở thân thẻ để truy cập bằng bàn phím/trình đọc màn hình; ảnh bìa là nội dung trang trí.

## Tổng quan gọn hơn

- Hàng đầu: tổng người dùng, tổng khóa học, lượt đăng ký (toàn lịch sử), doanh thu VND tháng hiện tại tính đến hôm nay theo giờ Việt Nam.
- Index gọi lại `IAdminRevenueQueryService` tuần tự sau Dashboard service; Controller chỉ ghép DTO, không tự truy vấn SQL hoặc định nghĩa lại cách ghi nhận doanh thu.
- Biểu đồ doanh thu dùng chung partial và JavaScript với trang báo cáo. Chỉ vẽ từ đầu tháng đến hôm nay; ngày trống là 0; không vẽ ngày tương lai.
- Số Admin được đếm từ Identity role tại SQL, không suy ra bằng phép trừ tổng tài khoản. Khóa/vô hiệu hóa được gom trong khối Người dùng.
- Tiến độ học tập, trạng thái khóa học và số đơn toàn lịch sử được giữ; loại số tổng lặp và các thẻ truy cập nhanh trùng sidebar. Thẻ số liệu thấp hơn và icon dùng nét SVG.
- 87 integration test Admin/layout/revenue liên quan đạt, có dữ liệu kiểm tra doanh thu tháng, loại tháng trước/ngoại tệ, không tính trùng thanh toán và giữ thông báo database rỗng. Bằng chứng: `dashboard-compact-final.trx` trong `TestResults/admin-ui-refinement/`.
- UAT tổng quan đạt 21 kiểm tra, đối chiếu doanh thu với trang báo cáo, ba vai trò với danh sách và ảnh 320/390/768/1440px: `TestResults/admin-dashboard-2026-10-24T13-34-18-350Z/`. UAT các mục Admin/danh mục đạt 31 kiểm tra: `TestResults/admin-refinement-2026-10-24T13-34-35-374Z/`. Đã xem ảnh tổng quan desktop và điện thoại; dữ liệu SQL ứng dụng không thay đổi trong lượt chỉnh tổng quan.

## Kiểm tra

Trang Lượt đăng ký bổ sung ngày đăng ký từ/đến, dựa trên `EnrolledAtUtc` (không dùng ngày hoàn thành). SQL áp dụng khoảng UTC tương ứng ngày Việt Nam trước Count/paging. Kết hợp tìm kiếm, trạng thái hoàn thành và courseId; giữ mọi điều kiện khi chuyển trang hoặc bỏ riêng lọc khóa học. Từ chối ngày sai, cho phép chỉ nhập một đầu ngày.

- 153 integration test liên quan đạt; gồm kiểm tra quyền, ba role được tạo, hash/audit/email trùng, tuần giao năm, tháng nhuận, ranh giới UTC+7, xếp hạng hai giảng viên, lọc ngày đăng ký một/hai đầu và phân trang giữ điều kiện. Sau khi đổi giao diện form đã chạy lại 10 test quản lý người dùng đạt. Bộ unit test hiện có đạt 542/542.
- Kết quả: `TestResults/admin-ui-refinement/admin-refinement-final.trx`, `create-user-styled.trx`, `admin-refinement-unit.trx`.
- Browser UAT chỉ đăng nhập và đọc, không tạo tài khoản trong database ứng dụng: `tests/browser/admin-refinement-uat.mjs`.
- Browser UAT đạt 25 kiểm tra: preset tuần/tháng/năm, biểu đồ đường ngày/tháng và chi tiết điểm, đổi ngày thủ công, lọc ngày khóa học/đăng ký, form ba vai trò, hiện/ẩn mật khẩu; ảnh tại 320/390/768/1440px. Bằng chứng: `TestResults/admin-refinement-2026-10-24T13-13-28-323Z/`. Đã xem ảnh desktop và điện thoại của form tạo người dùng.
- Kiểm tra phân nhóm CI: 70 lớp test, mỗi lớp đúng một shard. Security audit không có finding; `git diff --check` đạt.
- Sau khi làm rõ hai danh mục: 55 integration test layout/catalog/AdminOverview đạt (`catalog-clarity.trx`), UAT mở rộng 31 kiểm tra đạt (`TestResults/admin-refinement-2026-10-24T13-21-50-807Z/`), có kiểm tra Guest giữ lời giới thiệu ban đầu và Admin dùng tiêu đề danh mục công khai.
- Không migration, không seed thêm hoặc sửa dữ liệu ứng dụng trong phần thay đổi này. Chưa commit/push.
