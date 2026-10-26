# Hướng dẫn sử dụng Estudy cho buổi demo

Tài liệu cho Dương và người review thử hành trình bốn actor trên môi trường local
riêng. Dữ liệu thử không phải chứng chỉ năng lực và payment sandbox không thu tiền thật.

## Đăng nhập và dữ liệu demo

Mở `http://localhost:18080`. Guest là trạng thái chưa đăng nhập.
Các tài khoản sau dùng chung mật khẩu demo đã nhập lúc seed, không phải mật khẩu SQL.

| Tài khoản | Vai trò | Mục đích |
| --- | --- | --- |
| teacher1@demo.example.test | Teacher | Sở hữu Course, Question, Practice, Assessment mẫu |
| teacher2@demo.example.test | Teacher | Kiểm tra chặn sửa nội dung người khác |
| student1@demo.example.test | Student | Học khóa miễn phí đã ghi danh |
| student2@demo.example.test | Student | Thử mua khóa và quyền truy cập |
| admin1@demo.example.test | Admin | Quản lý tài khoản, giao dịch, audit |

Các khóa gồm Everyday English, Business English, English Starter A1,
English at Work A2 và Reading & Listening B1. Seeder có passage, audio nội bộ,
Practice và ba loại Assessment; không tạo lịch sử điểm giả. Dữ liệu test sau khi chạy
là kết quả thao tác thật. Muốn demo như ban đầu, dựng môi trường mới, không xóa lịch sử bằng SQL.

## Guest

Đăng xuất và mở Khóa học. Tìm/lọc, xem chi tiết khóa Free/Paid.
Guest chỉ thấy metadata, cấu trúc và giá; mở nội dung học yêu cầu đăng nhập.
Đăng ký tài khoản mới tạo Student, không tự chọn Teacher/Admin.

## Student

1. Với khóa miễn phí, mở chi tiết rồi ghi danh. Vào Khóa học của tôi, chọn Module/Lesson.
2. Mở nội dung, nghe audio nếu có, đánh dấu hoàn thành hoặc chưa hoàn thành.
3. Mở Practice, chọn đáp án và nộp; xem đáp án/giải thích, làm lại để cập nhật kết quả hiện tại.
4. Mở Assessment, đọc thời lượng/số lượt và bấm bắt đầu. Chọn đáp án, đợi báo đã lưu.
5. Refresh hoặc rời trang rồi Resume: cùng Attempt và deadline cũ. Xác nhận Submit để chấm.
6. Result hiển thị điểm tổng và kỹ năng đủ dữ liệu, không hiện đáp án từng câu như Practice.
7. Dashboard dùng kết quả đại diện theo quy tắc server; điểm cao nhất không nhất thiết là lần gần nhất.

Với khóa trả phí: tạo Order, chọn mô phỏng thất bại/hủy để thấy vẫn bị chặn nội dung.
Tạo lại đơn và mô phỏng thành công để được cấp Enrollment. Xem lịch sử giao dịch và vào học.
Không sửa URL trình duyệt để tự xác nhận thành công; server kiểm tra giao dịch trước cấp quyền.
Chỉ hoàn thành Lesson có thể chưa hoàn thành Course nếu Course còn yêu cầu Final đạt.

## Teacher

Mở Khóa của tôi hoặc ngân hàng câu hỏi. Teacher1 quản lý nội dung của mình,
Teacher2 dùng cùng URL sửa nội dung Teacher1 phải bị từ chối.

Để xuất bản Course: tạo Module Visible, tạo Lesson Draft, thêm Resource Text có nội dung
hoặc media hợp lệ, sau đó chuyển Lesson Published rồi Publish Course.
Tên Module/Description không thay thế Resource của Lesson.
Đề Practice/Assessment cần câu hỏi hợp lệ cùng owner; Preview không tạo Attempt Student.
Nội dung đã được sử dụng có thể bị khóa để giữ lịch sử, không sửa SQL để vượt khóa.

## Admin

Mở Quản trị, tìm/lọc User theo role/trạng thái, tạo giảng viên thử.
Chọn Chỉnh sửa để đổi vai trò hoặc trạng thái: Active mở khóa, Locked chặn đăng nhập,
Disabled ngừng sử dụng nhưng giữ dữ liệu. Hủy không lưu.
Mở Nhật ký và tìm email vừa sửa để xem người thao tác cùng dữ liệu Trước/Sau.
Admin xem giao dịch, không sửa nội dung Teacher và không làm Assessment thay Student.
Server chặn việc hạ quyền/khóa Admin hoạt động cuối cùng.

## Diễn tập và xử lý lỗi

- Không đăng nhập được: kiểm tra đúng cổng, email và mật khẩu seed; mỗi database có tài khoản riêng.
- Course không Publish: kiểm tra Lesson có Resource và Published trong Module Visible.
- Chưa vào được khóa Paid: kiểm tra payment đã Succeeded và Enrollment hợp lệ.
- Hết lượt thi: dùng đề khác hoặc môi trường mới; không xóa Attempt để tạo bằng chứng.
- Docker không kết nối: mở Docker Desktop, chờ Engine running rồi kiểm `/health`.
- Audio lỗi: ghi lại URL/trang và kiểm tra kho audio; không thay file của đề đã khóa.

## Bảng diễn tập người thật

| Nội dung | Người thực hiện | Ngày | Actual và bằng chứng |
| --- | --- | --- | --- |
| Guest và đăng ký | Chưa xác nhận | Chưa ghi | Chưa diễn tập |
| Student mua, học, luyện, thi | Chưa xác nhận | Chưa ghi | Chưa diễn tập |
| Teacher và ownership | Chưa xác nhận | Chưa ghi | Chưa diễn tập |
| Admin và audit | Chưa xác nhận | Chưa ghi | Chưa diễn tập |

Chỉ thay các ô sau khi Dương/người review thực hiện thật. Khi nộp từng môn,
ghi đúng người học môn đó vào cột phụ trách; không thay tên tác giả các tài liệu team.
