# Thiết kế luồng thanh toán sandbox

> Checkout sandbox local đã được triển khai; xem [contract thực tế và cách chạy](../project/checkout-local.md).
> Các bước server-to-server bên dưới là thiết kế gateway ngoài. Local simulator hiện gọi chung
> service xử lý callback với endpoint HTTP, dùng HMAC ngẫu nhiên trong bộ nhớ server.

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
4. Sau xác thực, kiểm tra dữ liệu callback và đối chiếu Order/Amount/Currency trước:
   không hợp lệ → 400. Sau đó **kiểm tra idempotency** theo `ProviderTransactionId`:
   → Có và mọi dữ liệu khớp: trả 200 OK sau commit. Callback hợp lệ nhưng xung đột
   bản ghi đã lưu → 409. Order đã kết thúc không bị event khác ghi đè.
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

- `IPaymentGateway` và implementation hiện dành cho local sandbox, gồm cả
  `CreateCallback(OrderCheckout, SandboxScenario)`. Tích hợp gateway thật còn cần
  điều chỉnh interface, kiểm tra provider trong service, cơ chế xác thực và cấu hình/key.
  Không thể chỉ thay implementation/DI để nhận callback provider khác.
- Callback accepted/replay ghi `AuditLog`; endpoint log outcome của callback bị từ chối,
  không ghi signature/secret hoặc body callback vào log.
- Ngoài phạm vi: coupon, subscription, payout, refund tự động.

## Lỗi transaction và gửi lại

Begin/operation/commit nằm trong vùng xử lý lỗi DB. Lỗi chính được ghi riêng bằng metadata
(operation, reference, phase, exception type, HResult/SQL number), không log message, chữ ký hoặc secret.
Rollback/dispose là cleanup được bảo vệ; lỗi cleanup không che lỗi chính. Cancellation của thao tác/request
được truyền lên, không đổi thành lỗi thanh toán. Cleanup dùng token riêng không bị request hủy.

Chỉ ACK thành công khi commit được xác nhận. Nếu commit đã xảy ra nhưng mất xác nhận, response là
503 `retry_later` (hoặc cancellation nếu request bị hủy); không tự retry hay tự báo thành công.
Bên gửi gửi lại cùng callback để transaction mới đối chiếu trạng thái đã lưu và xử lý idempotency.
Unique violation cũng trả lỗi tích hợp/503; lần gửi lại mới phân loại replay hoặc xung đột.
Lỗi dispose sau commit đã được xác nhận được log riêng, không đổi thành công đã xác nhận thành thất bại.

## Rủi ro đã biết

- Webhook đến trước khi Student quay lại trang kết quả: trang kết quả phải
  đọc trạng thái từ server (polling), không dựa vào redirect của cổng.
