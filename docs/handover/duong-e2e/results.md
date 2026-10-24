# Báo cáo E2E và bàn giao của Dương

Ngày kiểm tra 23/10/2026. Kết luận: fresh setup và tám nhóm E2E đã chạy đạt trên
môi trường Docker local riêng sau khi sửa lỗi responsive Admin. Cần Dương tự diễn tập
và leader review trước khi ghi nghiệm thu. Đây không phải kết quả CI GitHub.

## Môi trường và nguồn kiểm thử

Project Docker `elp-qa6-duong-20261023`, web 18080, SQL 15433, database và audio volume
mới riêng. Migration kết thúc mã 0, SQL healthy và web health 200. Payment sandbox bật,
hosted payment tắt. Web chính cổng 8080 và dữ liệu cũ được giữ nguyên.
Lượt cuối chạy 14:34:15–14:34:57 ngày 23/10/2026 theo giờ Việt Nam.
Commit gốc `7c2a23a`; có thay đổi chưa commit khi chạy. Report Student lưu hash source
trước/sau để xác minh code ổn định, không tự nhận commit gốc là bản chứa toàn bộ thay đổi.

## Kết quả thực tế

| Nhóm | Kết quả | Nội dung đã kiểm tra |
| --- | --- | --- |
| Auth | Đạt | Guest, đăng ký, đăng nhập, giữ khóa học đã chọn |
| Learning | Đạt | Enroll miễn phí, tiến độ, Final và hoàn thành Course |
| Practice | Đạt | Làm bài, feedback/giải thích, làm lại và quyền riêng tư |
| Assessment | Đạt | Passage/audio nội bộ, autosave hai tab, Submit, history, không lộ đáp án |
| Checkout | Đạt | Pending/fail/cancel chưa cấp quyền; success cấp quyền và giữ lịch sử |
| Responsive Student | Đạt | Dashboard, Course/Lesson, Practice/Result ở 320 và 390px |
| Roles | Đạt | Guest bị yêu cầu login; Teacher ownership/preview; Student/Admin đúng quyền |
| Admin | Đạt | Tạo Teacher, tìm/lọc, lock/unlock/disable, role, audit, hủy modal và responsive |

Lượt cuối lưu 35 ảnh. File gốc: `TestResults/duong-e2e/duong-20261023-final/run.json`;
report con, ảnh và SHA256SUMS nằm cùng thư mục. Kiểm tra hash trước khi gửi bằng chứng.
Không chứa mật khẩu, cookie hoặc token. Các report này là local automated E2E.

## Lỗi phát hiện và kiểm thử lại

Lần đầu `uat.mjs` dừng vì selector nhãn nút sandbox và giả định số lượng seed đã cũ.
Đó là lỗi kịch bản kiểm thử; dùng sáu nhóm Student mới cùng runner role/admin phù hợp
giao diện hiện tại. Giữ report thất bại ở thư mục lượt chạy ban đầu để đối chiếu.

E2E Admin phát hiện document rộng 613px ở viewport 390px. CSS trước sửa đã mất bố cục
mobile cho bảng, khiến nội dung vượt khung. Bản sửa chuyển `.admin-responsive` thành thẻ
có nhãn, giới hạn chiều rộng và cho email xuống dòng. Chạy lại toàn bộ tám nhóm đều đạt.

## Phân biệt đóng góp và giới hạn

Các service nghiệp vụ, seeder và sáu bộ Student đã có từ team. Phần mới là tùy chọn sandbox
của launcher fresh, runner role và runner tổng hợp, cập nhật admin test theo giao diện mới,
sửa responsive phát hiện qua E2E và hồ sơ riêng cho Dương.

Chưa xác nhận: diễn tập người thật của Dương, chữ ký UAT, remote CI của commit mới,
thanh toán provider thật và diễn tập khôi phục backup. Audio nội bộ được thử bằng luồng
browser của nhóm Assessment; không tuyên bố kiểm tra toàn bộ tài nguyên ngoài Internet.

## Kịch bản trình bày khoảng tám phút

1. Một phút: giới thiệu môi trường riêng, commit gốc, web 18080 và dữ liệu không ảnh hưởng 8080.
2. Một phút: Guest xem khóa; đăng nhập Student và enroll Free.
3. Hai phút: Pending/fail không cấp quyền, sandbox success mới mở khóa Paid; xem lịch sử.
4. Hai phút: học Lesson, Practice feedback, Assessment autosave/Resume và Result không lộ đáp án.
5. Một phút: Teacher1 quản lý nội dung, Teacher2 bị chặn sửa nội dung đó.
6. Một phút: Admin đổi trạng thái tài khoản thử, xem audit; chỉ ra ảnh mobile sau sửa.

Trước buổi demo, Dương cần tự chạy ít nhất một lượt và ghi actual vào bảng diễn tập của
hướng dẫn sử dụng. Nếu hết lượt Assessment, dùng đề khác hoặc môi trường mới;
không chỉnh Attempt/điểm trực tiếp trong SQL.

## Câu hỏi cần tự giải thích

- E2E khác unit/integration test ở đâu? E2E dùng browser và form thật để thử nhiều module cùng nhau.
- Tại sao tách database? Để tạo dữ liệu và thử lỗi mà không ảnh hưởng người đang làm việc.
- Vì sao không tin thanh toán thành công trên URL? Quyền học phụ thuộc xác minh payment/enrollment ở server.
- Vì sao Resume không đặt lại đồng hồ? Deadline đã lưu ở server và thuộc cùng một Attempt.
- Vì sao cần audit transaction? Để thay đổi tài khoản và nhật ký cùng thành công hoặc cùng rollback.
- Vì sao chưa ghi nghiệm thu? Test tự động không thay phần diễn tập và review của người thật.
