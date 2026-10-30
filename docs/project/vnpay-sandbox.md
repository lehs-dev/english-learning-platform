# VNPay Sandbox – sử dụng trang thanh toán chính thức

Đã loại bỏ form thẻ mô phỏng tự dựng.
Máy Dương dùng merchant trong `.env` riêng để chuyển trình duyệt tới trang
VNPay Sandbox chính thức. Không commit TmnCode/HashSecret vào Git.

Luồng dùng: khóa có phí → Tiếp tục thanh toán → Thanh toán qua VNPay Sandbox
→ chọn ngân hàng NCB, nhập thẻ test/OTP trên trang VNPay → quay lại Estudy.
Giữ đúng địa chỉ localhost và cổng đang dùng khi bắt đầu mua.
Đơn mô phỏng cũ vẫn giữ nguyên trong lịch sử. Bắt đầu lại từ chi tiết khóa học
để tạo đơn có provider `vnpay-sandbox`, không sửa provider của đơn cũ.

Các máy khác vẫn cần cấu hình merchant riêng hoặc dịch vụ chung nếu muốn
sử dụng chính trang VNPay; pull code không tự cấp thông tin merchant.

Estudy và SQL Server chạy local. Trình duyệt mở VNPay Sandbox để nhập thẻ/OTP
test, rồi quay về đúng localhost và cổng đang dùng. Server kiểm tra chữ ký,
merchant, chủ đơn, số tiền và thời gian trước khi ghi Payment/Enrollment.

## Cấu hình

Chế độ mặc định là Vnpay__UseIpn=false: không cần tunnel, tên miền public,
IPN hoặc dịch vụ trung gian. Vẫn cần Internet và thông tin merchant Sandbox.
Đây không phải cổng thanh toán chạy offline.

Điền trong .env đã được Git bỏ qua:

```dotenv
Vnpay__Enabled=true
Vnpay__UseIpn=false
Vnpay__TmnCode=<ma-merchant-test>
Vnpay__HashSecret=<chuoi-bi-mat-do-vnpay-cap>
Vnpay__PublicBaseUrl=
Vnpay__CheckoutMinutes=15
Payments__Enabled=false
CheckoutSandbox__Enabled=false
```

Không commit secret. Thành viên chưa có cấu hình merchant vẫn cần nhận cấu
hình local qua kênh riêng, không tự chia sẻ HashSecret qua Git. Compose chuyển
các biến vào web; dotnet run trực tiếp cần biến môi trường hoặc user-secrets.
Sau khi đổi cấu hình cần recreate web. Không có migration mới cho VNPay.

## Luồng sau mua

Giá chốt cho VNPay là số VND nguyên: lấy phần nguyên trước khi hiển thị xác
nhận, tạo Order và nhân 100 trong URL. Ví dụ dữ liệu cũ 99.999,98 VND có giá
chốt 99.999 VND, tham số `vnp_Amount=9999900`. Không cộng dồn sai số tiền.
Với Order cũ có phần lẻ chưa có Payment, chỉ phản hồi VNPay có chữ ký hợp lệ
và số tiền đúng bằng phần nguyên mới được chuẩn hóa trong cùng transaction;
ghi audit giá trước/sau. Thiếu thêm một đồng hoặc sai chữ ký vẫn bị từ chối.
Payment/doanh thu ghi đúng số tiền xác nhận, không ghi số lẻ cũ.

1. Student Active mua Course Published/Paid. Giá VND được chốt trong Order.
2. URL PAY 2.1.0 dùng OrderId dạng GUID N, Amount nhân 100, thời gian GMT+7,
   HMAC-SHA512. CreateDate ổn định theo thời điểm tạo Order khi refresh.
3. VNPay trả về /payments/vnpay/return ở đúng origin loopback đang dùng, kể cả
   cổng 5050 hoặc 8080. Không dùng X-Forwarded-Host tùy ý làm Return URL.
4. Return bắt buộc có chữ ký hợp lệ và đúng merchant. Payload được giữ trong
   TempData mã hóa, rồi chuyển tới Checkout.Result được bảo vệ bằng Student
   policy. Hết phiên thì đăng nhập và tiếp tục đúng đơn, không cần trả tiền lại.
5. Server kiểm tra chủ đơn, giá/provider/thời gian và dữ liệu ký. Chỉ cặp mã
   ResponseCode=00 và TransactionStatus=00 mới ghi Succeeded. Payment,
   Enrollment và audit cùng transaction; replay không tăng doanh thu/quyền học.
6. Thành công hiện nút vào học ngay. Failed/Cancelled không cấp quyền hoặc ghi
   doanh thu. Mua lại khi đã có Enrollment hợp lệ sẽ chuyển vào học.

Không tin status=success từ URL hoặc JavaScript. Thiếu/sai chữ ký không được
xác nhận. Đóng trình duyệt trước khi quay về có thể khiến đơn vẫn Pending:
đây là giới hạn của demo không dùng IPN. Không tự coi Pending là Failed và
không thanh toán lại nếu VNPay đã báo thành công. Mở lại Return hợp lệ để hoàn
tất; không sửa tay Payment trong SQL để ép kết quả.

Trang đơn theo dõi trạng thái cùng origin và tự cập nhật khi đổi trạng thái.
Back/Forward tới trang chờ cũ đọc lại trạng thái; link thanh toán đã mở trong
tab không tiếp tục mời mua lần nữa khi đang xác nhận. Hết phiên không tự
chuyển sang domain khác hoặc giả định thành công.

## Đăng nhập và quản trị

Docker volume data-protection-keys giữ khóa bảo vệ cookie qua recreate. Khi
chuyển container cũ sang volume này cần giữ lại khóa hiện tại trước khi
recreate nếu muốn bảo toàn phiên. Không commit khóa/cookie/bản sao khóa.

Giao dịch Admin hiển thị provider/mã receipt; lượt đăng ký có nguồn từ Payment
liên kết và link xem giao dịch, không suy nguồn từ giá Course hiện tại.
Dashboard và doanh thu giữ cách tính VND, mỗi Order thành công tính một lần.
Báo cáo có nhãn Sandbox; không trộn database demo với dữ liệu kinh doanh thật.

## IPN tùy chọn

Code vẫn hỗ trợ Vnpay__UseIpn=true: cần PublicBaseUrl HTTPS và khai báo endpoint
/payments/vnpay/ipn phía VNPay. Ở chế độ đó chỉ IPN xác nhận, Return không ghi
Payment. Đây không phải cấu hình local đang dùng. Không có QueryDr/refund trong
thay đổi này. Hosted gateway cũ và sandbox local vẫn được giữ khi VNPay tắt.

## Kiểm thử

- VnpayLocalReturnTests: thành công/hủy/thất bại, chữ ký sai, quyền sở hữu,
  hết phiên rồi đăng nhập, replay, mua lại và không cần IPN/public URL.
- VnpayCheckoutTests: IPN tùy chọn, rollback và kiểm tra báo cáo Admin.
- VnpayGatewayTests: chữ ký, số tiền, thời gian và localhost nhiều cổng.
- tests/browser/checkout-status.test.mjs: UI tự cập nhật, link mua lặp và hết phiên.
- Bằng chứng local trong TestResults/vnpay, không commit.

Nguồn chính thức:
- https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html
- Thẻ test: https://sandbox.vnpayment.vn/apis/vnpay-demo/
- Merchant test: https://sandbox.vnpayment.vn/devreg/

Chưa commit/push. Cần thử một giao dịch thẻ test thực tế quay về localhost;
automated test không thay cho lần xác nhận đó.

Kết quả bản local: 563/563 unit, 98/98 integration liên quan checkout/commerce/
doanh thu/security và 3/3 Chromium test đạt. Shard coverage đủ 74 lớp test;
security audit không có finding trong phạm vi được quét. Web local đã rebuild,
xác nhận phiên đăng nhập giữ nguyên qua recreate và tunnel tạm đã được tắt.
