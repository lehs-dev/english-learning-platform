# Giao diện khóa học theo vai trò — 22/10/2026

- Guest: khung trang phủ chiều rộng màn hình, bỏ nền tím và bo góc ngoài cùng. Màu của banner/nội dung được giữ theo giao diện hiện có.
- Student: sidebar có icon và nhãn chức năng, dùng cùng cấu trúc drawer với Teacher. `/Learning` và `/Courses` cùng hiển thị khóa đã tham gia ở trên và khám phá khóa mới ở dưới.
- Teacher/Admin: `/Courses` và chi tiết khóa học dùng layout theo vai trò, giữ sidebar và đánh dấu liên kết danh mục đang mở.
- Bộ lọc nằm trên danh sách khám phá; trên mobile có thể đóng/mở bằng chuột, cảm ứng hoặc bàn phím.

Khóa đã tham gia lấy qua kiểm tra Enrollment hiện có, bao gồm khóa Unpublished/Archived còn quyền học. Khóa chưa hoàn thành được xếp trước. Danh mục khám phá loại các ID đã tham gia **trước khi đếm và phân trang**; bộ lọc chỉ áp dụng phần khám phá, giữ các khóa đã tham gia để tiếp tục học.

## Kiểm tra

Build Release: 0 warning, 0 error. Bộ integration `CourseFeatureTests` và `RoleLayoutIsolationTests`: 21/21 pass trên database SQL GUID riêng do fixture tạo. Bao gồm loại khóa đã tham gia trước phân trang, giữ khóa Archived và sidebar theo vai trò.

Browser thật dùng các tài khoản demo hiện có, kiểm tra bốn vai trò trên desktop/mobile, sidebar, bộ lọc, trạng thái rỗng, hai đường dẫn Student và trang Dashboard:

Kết quả chốt: 14/14 kịch bản pass, 0 lỗi JavaScript; đã xem ảnh desktop/mobile và xác nhận sidebar còn hiển thị khi cuộn tới phần khám phá.

```powershell
# Web Development đã chạy ở cổng riêng 5108, có demo password trong cấu hình local.
cd tests/browser
npm.cmd run test:course-library
```

Có thể đổi địa chỉ bằng `ELP_UAT_URL` và thư mục kết quả bằng `ELP_UAT_RESULTS`. Runner chỉ nhận host local. JSON và ảnh nằm ở `artifacts/course-library`, không chứa browser storage/cookie; thư mục artifacts được Git ignore.
