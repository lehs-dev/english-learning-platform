# Checkout tuần 3 — sandbox local

## Tiêu chí nghiệm thu

- Student Active mua được Course Paid Published có giá hợp lệ, owner không Disabled.
- GET chỉ đọc; POST có antiforgery, nhận CourseId, lấy buyer từ claims và giá/currency từ server.
- Đã có quyền học hợp lệ chuyển tới Learning; Order luôn được đọc/thao tác theo ownership.
- POST → redirect → GET; bấm mua lặp/concurrent cùng quote tái dùng một Order chưa có Payment.
- Sandbox mặc định tắt; chỉ có thể bật trong Development. UI ghi rõ mô phỏng, không thu tiền thật.
- Callback được server ký/xác minh; chỉ Succeeded cấp Enrollment, cùng transaction với Payment.
- Callback giống nhau được nhận lại an toàn; dữ liệu khác/mã trùng và callback đến muộn bị từ chối.
- Test use case thực tế: ownership/giả mạo dữ liệu, lỗi callback, concurrency, rollback và hồi quy Free/Paid.
- UI cơ bản có kết quả/lỗi và nút Vào học; không mở rộng lịch sử giao dịch tuần 4.

## Implementation và ranh giới phối hợp

Khang nhận `POST /Checkout/CreateOrder` và UI checkout. Đây là sandbox local hoàn chỉnh,
chưa phải tích hợp gateway thật và chưa thực hiện pair với Sơn/Khánh.
Contract dưới đây dành cho review/pair tiếp theo; không thay schema hoặc chạy migration/seed ứng dụng.

`ICheckoutService`/DTO nằm trong Application/Commerce, `CheckoutService` và
`LocalSandboxGateway` trong Infrastructure/Commerce. `IPaymentGateway` hiện là contract local sandbox,
có thao tác tạo callback từ tình huống mô phỏng. Provider hiện tại chỉ là `local-sandbox`;
không gọi nhà cung cấp bên ngoài. Gateway thật còn cần chỉnh interface/provider/service và xác thực,
không chỉ thay implementation/DI; đợt sửa này không mở rộng kiến trúc gateway thật.

## Contract HTTP

Mọi trang/thao tác Checkout yêu cầu policy Student, dữ liệu không cache (`no-store`).
UserId lấy từ claims; service đọc lại role và AccountStatus ở database.

| Route | Request | Response/hành vi |
| --- | --- | --- |
| GET `/Checkout?courseId={guid}` | CourseId | 200 form đọc giá server; đã có quyền: redirect Learning; không đủ điều kiện: trang thông báo; không tạo Order |
| POST `/Checkout/CreateOrder` | form `CourseId`, antiforgery token | redirect `/Checkout/Order/{id}`; không bind Amount/Currency/StudentUserId/Status |
| GET `/Checkout/Order/{id}` | OrderId | 200 trạng thái từ DB; không thuộc buyer: 404; không ghi dữ liệu |
| POST `/Checkout/Simulate/{id}` | form `scenario=success\|failed\|cancelled`, antiforgery token | kiểm tra owner/điều kiện, gateway server tạo signed callback, xử lý rồi redirect Order |
| POST `/payments/webhook` | JSON signed callback | 200 accepted; 400 invalid_callback; 409 conflicting_event; 503 retry_later; sandbox tắt: 404 |

Guest được đưa tới Login; role sai bị từ chối. Không trả metadata Order của Student khác.
GUID rỗng/format lỗi hoặc scenario không hỗ trợ không được dùng để tạo giao dịch.

JSON callback (mẫu không chứa chữ ký thật):

```json
{
  "data": {
    "orderId": "GUID của Order",
    "amount": 199000,
    "currency": "VND",
    "provider": "local-sandbox",
    "providerTransactionId": "local-GUID-khong-dau-gach",
    "status": 1
  },
  "signature": "HMAC-SHA256-hex-do-server-tao"
}
```

`PaymentStatus`: Pending=0, Succeeded=1, Failed=2, Cancelled=3.
Callback chỉ chấp nhận ba trạng thái terminal; không tạo Pending Payment lúc tạo Order.
Canonical payload UTF-8: các trường theo thứ tự OrderId dạng N, Amount dạng G29 invariant,
Currency, Provider, ProviderTransactionId, Status dạng số; phân cách bằng LF, không LF cuối.
Verifier dùng so sánh constant-time. Provider/currency/transaction/amount/status được kiểm tra sau xác thực.

Khóa HMAC 256-bit ngẫu nhiên nằm trong singleton server, sinh khi bật sandbox, không hardcode,
không lưu file/DB và không gửi tới browser. Endpoint HTTP không có thao tác lấy khóa hoặc ký payload.
Browser chọn một *tình huống mô phỏng*; server tự đọc Order và tạo event/chữ ký.
Giả mạo `status=Succeeded` trong return URL hoặc POST mua không cấp quyền.
Local simulator gọi trực tiếp cùng `ProcessCallbackAsync` mà HTTP webhook sử dụng;
không cần loopback HTTP hoặc cấu hình URL có thể bị điều khiển để gửi callback.

## Quy tắc Order và tính nhất quán

- Giá quote là giá server tại POST; không nhận giá của browser. Currency hiện cố định VND,
  được lưu trên Order. Sau khi tạo, Payment phải khớp giá/currency đã chốt trên Order,
  không so lại giá Course mới.
- Cùng Student/Course/Amount/Currency: tái dùng Order chưa có Payment. POST refresh/lặp không tạo thêm.
- Failed/Cancelled là terminal; thử lại tạo Order mới. Giá Course đổi cũng tạo quote mới.
- Schema chưa có OrderStatus. Chưa có Payment nghĩa là chưa xử lý; trạng thái hiển thị lấy từ Payment.
- Callback đã ký phải khớp Order tồn tại, Amount và Currency. Mã transaction có tối đa 128 ký tự
  ASCII chữ/số hoặc `._:-`. Xác thực/dữ liệu/Order không hợp lệ trả 400 trước khi xét replay.
  Sau các kiểm tra đó, callback hợp lệ nhưng xung đột transaction hoặc trạng thái đã lưu trả 409.
- First terminal event wins: Order đã có Payment không nhận terminal event khác, kể cả transaction ID khác.
- Transaction SQL dùng `sp_getapplock` Exclusive, LockOwner=Transaction, tên `ELP.LocalCommerce`.
  Khóa cố ý chung cho sandbox nhỏ, bảo vệ giữa các request/process. Đây không phải chiến lược scale gateway thật.
- Payment lưu trước, Enrollment/Audit lưu sau nhưng trong *cùng transaction*; lỗi bước sau rollback Payment.
- Enrollment sử dụng range lock tương thích enroll Free và unique Student/Course. Enrollment hợp lệ/free
  có sẵn được giữ ID/progress; Enrollment gắn Payment không hợp lệ có thể nối Payment thành công mới.
- Unique Payment transaction ID và Enrollment là lớp bảo vệ cuối. Request gặp unique violation
  thực hiện cleanup và trả IntegrationError/503, không tự retry hay đối chiếu để báo thành công ngay.
  Ở **lần gửi lại callback**, transaction mới đọc trạng thái đã lưu để phân loại replay/xung đột.
  Nếu writer ngoài contract chỉ ghi Payment, replay hợp lệ có thể khôi phục Enrollment.
- Begin/operation/commit được bảo vệ. Rollback/dispose được xử lý riêng, không che lỗi chính;
  lỗi DB trả IntegrationError/503. Cancellation được truyền lên, không coi là thất bại thanh toán.
  Log chỉ ghi metadata đủ chẩn đoán, không ghi exception message/body/signature/key.
- Commit chưa được xác nhận thì không trả thành công, dù DB có thể đã commit. Bên gửi phải gửi lại
  cùng callback để đối chiếu trạng thái đã lưu. Không tự retry hoặc suy đoán kết quả commit.
  Dispose lỗi sau commit đã xác nhận chỉ được log; không đổi kết quả thành công đã xác nhận.
- Callback accepted/replay ghi AuditLog trong transaction; endpoint log outcome của callback bị từ chối,
  không log body, signature, key hoặc credential.
- Return/GET chỉ hiển thị DB; không cấp Enrollment. Learning vẫn kiểm tra quyền cũ.

## Bật local bằng Bash

Dùng terminal Dev Container đã có connection environment đúng database local. Không sao chép/in connection string.
Lệnh sau **chỉ dành cho người dùng tự chạy** khi muốn thao tác mua thử trên dữ liệu local đã có:

```bash
ASPNETCORE_ENVIRONMENT=Development \
DOTNET_ENVIRONMENT=Development \
LearningDemo__Enabled=false \
CheckoutSandbox__Enabled=true \
dotnet run --project src/EnglishLearningPlatform.Web --no-launch-profile --urls http://localhost:8081
```

8081 tránh xung đột website 8080 đang chạy. App mới kế thừa cấu hình kết nối;
không tự migrate/seed. Không cần nhập secret sandbox: server tự sinh khóa trong bộ nhớ.
Môi trường Production/Staging luôn chặn sandbox, ngay cả khi cờ true. Không có cờ mặc định true.
Ctrl+C chỉ dừng instance 8081; các biến inline không tồn tại sau lệnh. Nếu đã export riêng:

```bash
unset CheckoutSandbox__Enabled
```

Khởi động lại server làm khóa cũ mất hiệu lực; Order/Payment đã lưu vẫn giữ nguyên.
Sandbox này dành cho một instance local. Không đưa nó lên production hoặc dùng nó như bằng chứng thu tiền thật.

## Checklist browser

Nếu dữ liệu demo đã có: dùng Student2 chưa enroll Paid; Teacher1 sở hữu khóa,
Teacher2 để thử sai role và Student1/Student2 để thử sai owner. Dùng credential local đã có;
checkout không tạo tài khoản demo và không chạy seed.

1. Student2 → Catalog → Business English → Mua khóa học: thấy tên/Teacher/giá VND và nhãn mô phỏng.
2. GET/refresh checkout không tạo Order. Tiếp tục → Order chưa thanh toán, chưa có quyền Lesson.
3. Mở lại checkout, bấm mua lần nữa: cùng quote trở về cùng mã Order.
4. Mô phỏng thất bại: thông báo lỗi, không vào học. Thử lại: mã Order mới.
5. Mô phỏng hủy: thông báo hủy, không vào học. Thử lại rồi thành công: nút Vào học mở nội dung.
6. Refresh/Back/return URL có status giả không tạo thêm Payment/Enrollment hoặc đổi kết quả đã lưu.
7. Student khác mở URL Order: 404. Teacher/Admin không có quyền checkout.
8. Tắt cờ: checkout báo chưa bật, POST/callback không xử lý thanh toán.
9. 390px: tên dài/mã Order xuống dòng, nút wrap, không tràn ngang. Tab/Enter thao tác link/nút;
   thông báo lỗi có role=alert, thành công/hủy có role=status.

## Test và phần cần review

`CheckoutFlowTests` dùng `CheckoutTestFactory` và DatabaseFixture: migration/dữ liệu chỉ nằm
trong database `EnglishLearningPlatformTests_<GUID>` riêng, dọn khi xong. Không dùng database ứng dụng.
Test tạo tài khoản test bằng helper hiện có; không cần demo seed.

```bash
dotnet build EnglishLearningPlatform.sln
# Inherit server settings silently; fixture always substitutes its own database name.
ELP_TEST_SQLSERVER="$ConnectionStrings__DefaultConnection" \
LearningDemo__Enabled=false \
dotnet test tests/EnglishLearningPlatform.IntegrationTests --no-build -- xUnit.ParallelizeTestCollections=false
dotnet test tests/EnglishLearningPlatform.UnitTests --no-build
git diff --check
```

Kết quả kiểm tra của phiên triển khai: build 0 warning/0 error, 77 integration tests
(39 checkout + 38 hồi quy) và 34 unit tests pass, không skip; whitespace pass cả file mới.
Lượt toàn bộ integration chạy song song ban đầu có timeout ở test authoring cũ; chạy tuần tự
theo lệnh trên đạt. Các test callback/order đồng thời vẫn dùng Task.WhenAll bên trong test.

Sau sửa findings transaction/contract: build 0 warning/0 error, **96 integration tests**
(58 checkout, gồm 19 trường hợp hồi quy transaction mới, và 38 hồi quy khác), **34 unit tests** pass,
không skip. Fault injection chỉ tác động transaction trên database test GUID riêng; không tắt SQL/container local.
Test mới kiểm tra lỗi begin trước/sau cấp phát transaction, provider error bọc, lỗi DB kèm rollback/dispose lỗi,
transaction đã kết thúc, cancellation trước begin/trong commit/bọc trong exception, mất xác nhận commit rồi replay,
và dispose lỗi sau commit đã xác nhận. Log lỗi gốc và cleanup được kiểm tra riêng, không có exception message/secret.
Không gây mất mạng hoặc tắt SQL thật; sự cố begin/commit/cleanup được mô phỏng quanh transaction SQL thật.

Playwright/Chromium chạy trong container công cụ riêng, trên HTML thật được test HTTP xuất ra
(không dùng database ứng dụng): sáu trạng thái checkout/pending/success/failed/cancelled/error
ở 390px và 1280px, 12 lượt đều không tràn ngang, nút/link đến được bằng Tab, skip link hoạt động
bằng Enter, thông báo có role phù hợp. Đã xem screenshot mobile/desktop. Đây là kiểm tra render
và bàn phím; form POST và việc cấp quyền được xác minh qua HTTP integration tests riêng,
chưa phải một phiên browser mua thử trên database local hiện tại của Khang.

Review cùng Sơn/Khánh: quote hết hạn/giá đổi, Course đổi trạng thái lúc thanh toán,
thanh toán nhiều Order cho cùng khóa, provider transaction namespace, retry/deadlock policy,
secret bền vững/key rotation cho gateway thật. Chưa có refund, provider thật, timeout payment,
history hoặc toàn bộ UI kết quả tuần 4. Không nhận sandbox là đã pair hoặc tích hợp nhà cung cấp thật.
