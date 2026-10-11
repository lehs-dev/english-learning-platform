# SP11 · Hướng dẫn sử dụng

Nền tảng học tập và đánh giá năng lực tiếng Anh trực tuyến

Phiên bản bàn giao: Release Candidate cho đợt bảo vệ 16/10/2026. Ngày soạn: 11/10/2026, múi giờ Asia/Saigon. Phụ trách tổng hợp: Lê Hoàng Sơn — MSSV 3123560077. Tài liệu mô tả mã nguồn của nhánh thực thi; kết quả nghiệm thu thực tế được ghi riêng tại SP14.

## 1. Bắt đầu

Mở địa chỉ do người vận hành cung cấp. Demo local thường dùng http://localhost:8081; Docker Compose mặc định http://localhost:8080. Từ trang chủ, chọn danh mục khóa học, đăng nhập hoặc đăng ký. Không nhập thông tin thẻ ngân hàng thật trong sandbox.

Hệ thống có ba vai trò tài khoản: Student, Teacher và Admin. Guest là người chưa đăng nhập. Mỗi tài khoản dùng đúng một vai trò đang hoạt động. Tài khoản Locked hoặc Disabled không thể tiếp tục truy cập bằng phiên cũ sau khi trạng thái thay đổi.

| Vai trò | Việc chính |
| --- | --- |
| Guest | Xem danh mục và chi tiết khóa công khai; đăng ký Student; đăng nhập. |
| Student | Ghi danh hoặc mua khóa, học bài, lưu tiến độ, làm bài thi và xem kết quả của mình. |
| Teacher | Tạo và quản lý khóa, module, bài học, tài nguyên thuộc sở hữu; xem trước nội dung. |
| Admin | Tra cứu giao dịch, tài khoản và trạng thái; không sửa nội dung giảng dạy của Teacher. |

## 2. Đăng ký, đăng nhập và hồ sơ

1. Mở /Account/Register, nhập họ tên, email, mật khẩu và xác nhận. Đăng ký tự phục vụ tạo tài khoản Student; không chọn Teacher/Admin tại màn hình đăng ký.
2. Mật khẩu phải đáp ứng yêu cầu Identity: tối thiểu 8 ký tự, có chữ hoa, chữ thường, chữ số và ký tự đặc biệt theo cấu hình Identity. Email phải chưa được sử dụng.
3. Mở /Account/Login, nhập email và mật khẩu. Khi đăng nhập từ một trang cần quyền, hệ thống đưa về địa chỉ hợp lệ sau đăng nhập.
4. Mở /Profile để sửa họ tên. Bấm lưu; vai trò và trạng thái tài khoản không được tự thay qua hồ sơ.
5. Đăng xuất bằng nút Đăng xuất. Sau khi dùng máy chung, đóng cửa sổ trình duyệt.

Nếu đăng nhập thất bại nhiều lần, ngừng thử liên tục và kiểm tra email/mật khẩu hoặc trạng thái tài khoản với quản trị viên. Không đưa mật khẩu hay cookie lên ảnh minh chứng.

## 3. Guest: tìm và xem khóa học

Mở /Courses. Dùng tìm kiếm, bộ lọc và phân trang để chọn khóa. Mở chi tiết để xem tên, mục tiêu, cấp độ, kỹ năng, chủ đề, giá và cấu trúc nội dung công khai. Catalog chỉ hiển thị khóa Published đủ điều kiện; nội dung Draft hoặc Hidden không xuất hiện cho học viên.

Guest có thể đọc metadata, nhưng mở bài học cần đăng nhập và quyền truy cập hợp lệ. Khi được chuyển đến trang đăng nhập, đăng nhập Student rồi quay lại khóa để ghi danh hoặc mua khóa.

## 4. Student: ghi danh khóa miễn phí

1. Đăng nhập Student và mở khóa miễn phí tại /Courses.
2. Chọn ghi danh. Yêu cầu được kiểm tra tại server; bấm lặp không tạo hai Enrollment cho cùng Student và Course.
3. Mở /Learning, chọn khóa trong Khóa học của tôi.
4. Mở module hoặc bài học khả dụng. Có thể học theo bất kỳ thứ tự nào; không cần hoàn tất bài trước để mở bài sau.

Dữ liệu demo: student1@demo.example.test đã được ghi danh Demo · Everyday English. Các tài khoản này chỉ xuất hiện khi người vận hành bật seeder Development.

## 5. Student: mua khóa trong sandbox

1. Đăng nhập student2@demo.example.test và mở Demo · Business English, giá 199.000 VND.
2. Chọn mua khóa và tạo đơn. Màn hình hiển thị số tiền và mã đơn để đối chiếu.
3. Nếu môi trường Development đã bật local sandbox, chọn mô phỏng thanh toán thành công. Có thể dùng kịch bản thất bại hoặc hủy để minh họa trước khi tạo đơn mới.
4. Đọc trang kết quả /Checkout/Result/{orderId}. Chỉ sau khi server xác minh thanh toán thành công và cấp Enrollment, chọn vào học.
5. Mở /Learning để xác nhận khóa xuất hiện, mở bài Business introductions.
6. Refresh trang kết quả và trang khóa; hệ thống giữ một Enrollment. Xem /Checkout/History để tra cứu các đơn của mình.

Nút quay về từ cổng hoặc tham số URL không tự xác nhận đã thanh toán. Khi thanh toán bị tắt/chưa cấu hình, màn hình thông báo tình trạng; người dùng không được cấp quyền học bằng cách sửa URL. Sandbox local không thu tiền thật. Hosted gateway hiện là hợp đồng HMAC riêng; không đại diện cho giao dịch thật qua VNPay/MoMo.

Nếu giao dịch thành công nhưng nội dung bị ngừng phát hành, tài khoản không đủ điều kiện hoặc khóa chưa mở quyền, giữ mã đơn và liên hệ quản trị viên. Không thanh toán lặp để chữa lỗi quyền.

## 6. Student: học và theo dõi tiến độ

Tại /Learning/Course/{courseId}, chọn bài Published trong module Visible. Nội dung có thể gồm văn bản, đường dẫn, âm thanh hoặc video. Nếu media ngoài mất mạng, học phần văn bản còn khả dụng.

Sau khi học, chọn đánh dấu hoàn thành. Chọn chưa hoàn thành để đổi trạng thái hiện tại nếu cần ôn lại. Tiến độ được tính bằng số bài khả dụng đã hoàn thành chia cho tổng bài khả dụng. Bài Draft/Hidden và module Hidden không nằm trong mẫu số. Khóa chưa có bài khả dụng không được coi là hoàn thành 100%.

Progress 100% chưa luôn đồng nghĩa đã hoàn thành khóa. Với khóa có bài thi cuối khóa, cần vừa hoàn thành mọi bài khả dụng vừa đạt Final. Khi đủ điều kiện, trang khóa hiển thị huy hiệu và ngày hoàn thành. Mốc CompletedAtUtc ghi lần đầu được giữ khi Teacher thêm bài mới hoặc Student đánh dấu lại chưa hoàn thành; tiến độ hiện tại vẫn phản ánh nội dung mới.

## 7. Student: làm bài thi cuối khóa

1. Tại trang khóa, xem khối Bài thi cuối khóa: tên, thời lượng, số câu, số lượt đã dùng/tối đa, điểm cao nhất và trạng thái.
2. Chọn xem/làm bài để mở /Assessment/Take/{assessmentId}. Trang tổng quan cho phép xem lịch sử lượt đã chấm và mở kết quả.
3. Chọn bắt đầu hoặc tiếp tục. Lượt thi được tạo/resume bằng thao tác POST; chỉ mở trang GET không tạo lượt thi.
4. Chọn một phương án cho từng câu. UI lưu đáp án qua autosave và có nút lưu đáp án. Kiểm tra thông báo lưu; khi mất mạng, khôi phục kết nối và lưu lại trước deadline.
5. Theo dõi thời gian còn lại. Deadline được lưu ở server; đổi giờ máy hoặc tắt tab không kéo dài thời gian thi.
6. Chọn nộp bài. Hết thời gian, giao diện yêu cầu tự nộp; server xử lý hết hạn dựa trên deadline và đáp án đã lưu.
7. Mở /Assessment/Result/{attemptId} để xem điểm tổng, đạt/chưa đạt, thời điểm hoàn tất và điểm kỹ năng.

Assessment không hiển thị đáp án đúng hoặc giải thích từng câu. Mỗi lượt và kết quả chỉ thuộc học viên đang đăng nhập; sửa attemptId của người khác không cấp quyền. Nộp lặp không chấm hai lần. Khi đã hết lượt, dùng lịch sử để ôn và liên hệ Teacher theo quy định; không xóa lịch sử để làm lại.

Final demo có 5 câu, trọng số bằng nhau, 15 phút, tối đa 3 lượt và ngưỡng 80%. Đúng ít nhất 4/5 câu đạt Final. Grammar gồm 3 câu, Vocabulary gồm 2 câu. Điểm tổng là tỷ lệ points đúng; kết quả đạt so với ngưỡng trước làm tròn. Kết quả hiển thị 80,00% không tự chứng minh đạt nếu giá trị thô thấp hơn ngưỡng trong đề khác.

## 8. Teacher: quản lý khóa và xem trước

1. Đăng nhập teacher1@demo.example.test; mở /TeacherCourses.
2. Chọn tạo khóa hoặc sửa khóa thuộc sở hữu. Nhập metadata, giá nếu là Paid, kỹ năng và chủ đề theo form.
3. Mở quản lý khóa để thêm/sửa module, bài học và tài nguyên; đặt thứ tự hiển thị, Visible/Hidden và Published/Draft theo nhu cầu.
4. Dùng xem trước để kiểm tra nội dung của mình, kể cả nội dung chưa công khai.
5. Publish khi đủ điều kiện; Unpublish để rút khỏi truy cập mới hoặc Archive để giữ lịch sử theo quy tắc ứng dụng.

teacher2@demo.example.test là tài khoản Teacher đối chứng. Tài khoản Teacher khác không được sửa khóa của teacher1 dù biết ID. Admin cũng không được dùng đường dẫn Teacher để sửa nội dung. UI bàn giao tập trung Course authoring và Student Assessment; công cụ authoring ngân hàng câu hỏi/Practice/Final theo thiết kế đích không được suy ra là đã có từ màn hình Student.

## 9. Admin: tra cứu tài khoản và thanh toán

Đăng nhập admin1@demo.example.test. Mở /Admin để vào khu quản trị. Mở /Admin/Users để tra cứu email, vai trò và trạng thái; mở /Admin/Transactions để tra cứu đơn, người mua, khóa, số tiền và trạng thái Payment. Dùng mã đơn từ học viên khi đối chiếu.

Admin không xem đáp án đúng trong Assessment và không sửa/xóa nội dung Course, Lesson hoặc Question của Teacher. Luồng quản trị bàn giao là tra cứu; không nhận các thao tác đổi role/khóa/xóa user là đã triển khai nếu màn hình không cung cấp.

## 10. Xử lý tình huống thường gặp

| Hiện tượng | Thao tác |
| --- | --- |
| Bị chuyển tới đăng nhập | Đăng nhập đúng vai trò; kiểm tra tài khoản không Locked/Disabled. |
| Không vào được bài đã mua | Kiểm tra trang kết quả thanh toán và mã đơn; quyền dựa trên xác nhận server. |
| Progress 100% nhưng chưa hoàn thành | Kiểm tra Final và điểm đạt; hoàn thành cả hai điều kiện. |
| Attempt hết hạn | Mở kết quả hoặc bắt đầu lượt mới nếu còn lượt; deadline cũ được giữ. |
| Lưu/nộp lỗi mạng | Khôi phục kết nối, kiểm tra thông báo và lịch sử; nộp lặp được xử lý an toàn. |
| Không có audio/video | Dùng nội dung văn bản; báo URL tài nguyên cho Teacher. |
| Teacher không thấy khóa | Kiểm tra tài khoản chủ sở hữu; không đổi URL để vượt quyền. |

Khi báo lỗi, ghi thời điểm Asia/Saigon, URL đã che thông tin nhạy cảm, vai trò, bước tái hiện, mã đơn/attempt nếu liên quan và ảnh màn hình đã che dữ liệu cá nhân. Không gửi mật khẩu, chữ ký HMAC, connection string hoặc cookie.

## 11. Hồ sơ kèm theo

SP13 mô tả triển khai và vận hành. SP14 chứa matrix UAT và liên kết bằng chứng thật. SP15 ghi yêu cầu/quy tắc dữ liệu. Tệp PDF/DOCX được xuất từ cùng nguồn Markdown để dễ cập nhật. Hướng dẫn demo có tại docs/project/son-demo-fallback.md.
