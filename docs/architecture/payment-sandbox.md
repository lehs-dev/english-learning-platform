# Thiết kế luồng thanh toán sandbox

## Mục tiêu
Cho phép Student mua khóa trả phí qua cổng thanh toán giả lập (sandbox),
đảm bảo: (1) chỉ webhook đã xác minh mới được ghi nhận thanh toán,
(2) callback gọi lặp không tạo Payment/Enrollment trùng.

## Luồng

1. Student bấm "Mua khóa học" (khóa có `IsPaid = true`) → server tạo `Order`
   (chưa có `Payment`), redirect sang trang thanh toán của cổng sandbox.
2. Student thanh toán ở cổng sandbox → cổng gọi webhook server-to-server
   về `POST /payments/webhook`, kèm `providerTransactionId`, số tiền,
   chữ ký (signature).
3. Server **xác minh chữ ký** bằng secret chỉ server và cổng biết.
   Chữ ký sai → trả 400, không xử lý gì thêm. Không bao giờ tin dữ liệu
   từ browser/client để ghi nhận thanh toán.
4. **Kiểm tra idempotency**: `Payment` với `ProviderTransactionId` đã tồn tại?
   → Có: trả 200 OK và dừng (callback lặp an toàn).
5. Chưa tồn tại → trong **một transaction**: tạo `Payment` (`Succeeded`) +
   tạo `Enrollment` (nối `PaymentId`) → commit. Hoặc cả hai cùng thành công,
   hoặc không gì được ghi.
6. Trả 200 OK cho cổng. Student được redirect về trang kết quả.

## Hàng rào chống trùng (defense in depth)

- Tầng database: unique index `IX_Payment_ProviderTransactionId`
  (đã có test `CommercePaymentTests.DuplicateProviderTransactionId_IsRejected`).
- Tầng ứng dụng: kiểm tra tồn tại ở bước 4 trước khi insert.
- `Enrollment` đã có unique `(StudentUserId, CourseId)` từ trước.

## Thiết kế mở rộng

- Cổng thanh toán là interface `IPaymentGateway`; sandbox chỉ là một
  implementation giả lập. Sau này thay VNPay/MoMo thật: implement interface
  mới, không sửa code cũ (Dependency Inversion).
- Mọi webhook đều được ghi `AuditLog` để truy vết.
- Ngoài phạm vi: coupon, subscription, payout, refund tự động.

## Rủi ro đã biết

- Webhook đến trước khi Student quay lại trang kết quả: trang kết quả phải
  đọc trạng thái từ server (polling), không dựa vào redirect của cổng.
