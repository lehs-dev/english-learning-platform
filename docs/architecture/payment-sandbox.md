# Enrollment trả phí và hợp đồng hosted checkout

Cập nhật 08/10/2026. Luồng Order, checkout, xác minh webhook và Enrollment đã có. Adapter `HostedPaymentGateway` dùng hợp đồng HMAC bên dưới với một cổng thanh toán ngoài. Repository không chứa trang giả lập trả tiền hoặc endpoint cho browser tự xác nhận thành công. Chưa có adapter VNPay/MoMo hay credentials cho provider thật. Mặc định thanh toán tắt; nút mua hiển thị thông báo khi cấu hình chưa sẵn sàng và không tạo Order.

## Cấu hình

Áp dụng migration `AddCheckoutOrderMetadata` trước khi chạy. Migration bổ sung Provider và ExpiresAtUtc cho Order, không sửa migration cũ hoặc quyền học đã cấp.

Đặt các giá trị sau qua environment hoặc cấu hình local. Secret chỉ được đặt ở server và cổng ngoài, không commit:

| Khóa | Giá trị |
| --- | --- |
| `Payments__Enabled` | `true` |
| `Payments__Provider` | Tên provider 1–64 ký tự ASCII chữ/số/`-_.` |
| `Payments__CheckoutUrl` | URL checkout của cổng ngoài |
| `Payments__PublicBaseUrl` | URL public của ứng dụng; không suy ra từ Host header |
| `Payments__SigningSecret` | Secret riêng, ít nhất 32 ký tự |
| `Payments__CheckoutMinutes` | Thời hạn đơn, mặc định 30 phút, cho phép 1–1440 |

URL phải là HTTPS, không user info/query/fragment. HTTP loopback chỉ được phép trong Development. Cổng ngoài phải triển khai đúng hợp đồng này. Nếu dùng provider có protocol khác, implement `IPaymentGateway` phù hợp và đổi đăng ký DI; không gửi hợp đồng HMAC tùy chỉnh trực tiếp tới VNPay/MoMo.

## Luồng HTTP

1. Student Active gửi POST có CSRF tới `/Checkout/Start/{courseId}`. Server kiểm tra khóa Published, Paid, giá hợp lệ và owner không Disabled. Student/giá lấy ở server. Enrollment hợp lệ đã có thì chuyển vào khu học tập.
2. Server tái sử dụng Order chưa có Payment, còn hạn, cùng Student/Course/provider/giá, hoặc tạo Order mới. Giá VND được chốt trong Order. Chưa tạo Payment/Enrollment. Khóa SQL và transaction ngăn nhiều POST đồng thời tạo nhiều Order chờ giống nhau.
3. `/Checkout/Result/{orderId}` chỉ chủ đơn Student Active được xem. Trang hiển thị số tiền và liên kết sang cổng ngoài; GET luôn đọc trạng thái đã lưu. Các tham số `status`, `paid` hoặc return redirect không cấp quyền.
4. Cổng gửi POST `/payments/webhook`, body JSON UTF-8, header `X-Payment-Signature` là HMAC-SHA256 của **đúng bytes body**, hex 64 ký tự. Server so chữ ký bằng phép so thời gian cố định trước khi xử lý JSON. Giới hạn body 16 KiB.
5. Trong một transaction, server kiểm tra Order/provider/amount/currency và thời điểm thanh toán; ghi Payment đã xác minh và Enrollment nếu đủ điều kiện. Signature sai, dữ liệu sai hoặc giao dịch trùng cho đơn khác không cấp quyền học.
6. Student bấm kiểm tra trạng thái trên trang kết quả. Chỉ Payment Succeeded được xác minh và Enrollment hợp lệ mới mở nội dung.

## Chữ ký checkout

Query gồm các trường theo đúng thứ tự: `orderId`, `amount`, `currency`, `expiresAt`, `returnUrl`, `webhookUrl`, rồi `signature`. Amount dùng invariant culture, đúng 2 chữ số thập phân; expiresAt là Unix seconds. Giá trị từng trường được encode bằng URI percent encoding (`Uri.EscapeDataString`), ghép `key=value` bằng `&`. Signature là HMAC-SHA256 của chuỗi ghép này bằng UTF-8, hex chữ thường, dùng SigningSecret. Cổng phải kiểm tra chữ ký và hạn trước khi nhận tiền. Không cho người dùng tự thay giá, đơn hoặc URL callback.

Webhook JSON gồm `orderId` (GUID), `providerTransactionId` (1–128 ký tự, không ký tự điều khiển), `amount` (số dương, tối đa 2 chữ số thập phân), `currency` (`VND`), `status` (**1 = Succeeded, 2 = Failed**, giá trị numeric của enum hiện tại), `occurredAtUtc` (ISO 8601). Chỉ nhận sự kiện kết thúc Succeeded/Failed. Pending/Cancelled chưa có hợp đồng xử lý trong adapter này.

OccurredAt phải từ thời điểm tạo Order (cho phép lệch đồng hồ tối đa 1 phút) tới hạn Order; không được ở tương lai quá 5 phút. Webhook retry có thể đến sau hạn nếu thời điểm giao dịch đã ký nằm trong hạn. Giá so với quote Order lúc mua, không thay bằng giá Course mới; vẫn kiểm tra Course hiện Published/Paid và giá hiện tại hợp lệ trước khi cấp Enrollment.

## Idempotency và lifecycle

- Unique index Payment.ProviderTransactionId và Enrollment(StudentUserId, CourseId) giữ hàng rào database. Luồng app khóa Course trước, rồi khóa range transaction ID trong cùng transaction Serializable. Request lặp cùng giao dịch/Order/amount/status trả 200 và không ghi thêm.
- Transaction ID đã gắn đơn khác hoặc cùng Order đã có Payment Succeeded nhưng nhận success khác trả 409. Sai chữ ký/body/quote/thời gian trả 400; không thấy Order trả 404; gateway tắt/chưa cấu hình trả 503.
- Payment Failed được lưu nhưng không tạo Enrollment. Student có thể tạo Order mới để thử lại. Không chuyển một receipt Failed sang Succeeded với cùng transaction ID; provider phải gửi kết quả cuối ổn định.
- Nếu nhận receipt thành công nhưng Course đã Unpublished/Archived, owner Disabled hoặc Student không Active/không còn đúng role, vẫn lưu receipt đã xác minh và AuditLog, không cấp Enrollment mới. Trang kết quả hướng dẫn liên hệ hỗ trợ với mã đơn. Refund tự động nằm ngoài phạm vi.
- Nếu đã có Enrollment hợp lệ, giao dịch khác không tạo Enrollment thứ hai. Enrollment free cũ vẫn hợp lệ khi đổi khóa sang Paid. Enrollment Paid giữ liên kết payment/quote gốc khi giá khóa thay đổi.
- Order cũ trước migration có Provider rỗng/ExpiresAt null không được xác nhận qua adapter này; Enrollment Paid cũ vẫn được kiểm tra theo các quy tắc quyền học hiện có.

## Kiểm tra

`CheckoutFeatureTests` kiểm tra nhiều POST tạo đơn và nhiều webhook đồng thời, chữ ký/amount/currency sai, transaction dùng lại cho đơn khác, sự kiện quá hạn, quote sau đổi giá, callback return giả, quyền xem đơn, CSRF, payment Failed, khóa bị rút và gateway chưa cấu hình. Đây là test server với thông báo ký từ fixture, không chứng minh kết nối một provider thật.
