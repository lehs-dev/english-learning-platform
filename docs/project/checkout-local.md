# Checkout local sau hợp nhất main

## Phạm vi

Một service/controller và UI Estudy dùng chung luồng Order → receipt xác thực → Payment/Enrollment.
Hosted contract và progress từ main được giữ; không có hai checkout service/controller cạnh tranh.
`IPaymentGateway` theo PaymentModels của main; `LocalSandboxGateway` là adapter riêng,
không được dùng để tạo hoặc xác nhận receipt hosted. Provider thật/pair Sơn/Khánh chưa thực hiện.

## Routes và chọn chế độ

- GET `/Checkout?courseId={guid}` chỉ đọc xác nhận, giá/Teacher từ database, đã có quyền hướng tới Learning.
- POST `/Checkout/CreateOrder`: CourseId + antiforgery; buyer từ claims. Hosted được ưu tiên khi cấu hình sẵn sàng,
  nếu không thì local chỉ khi Development + CheckoutSandbox enabled; cả hai tắt không tạo Order.
- POST `/Checkout/Start/{courseId}` giữ contract main, luôn hosted; không fallback local.
- GET `/Checkout/Order/{id}` và `/Checkout/Result/{id}` dùng cùng view, chỉ Student Active sở hữu Order.
  Adapter được chọn theo Order.Provider đã lưu; GET/return query không cấp quyền.
- POST `/Checkout/Simulate/{id}`: scenario success/failed/cancelled + antiforgery, chỉ Order local.
  Order hosted bị chặn dù cờ local đang bật. UI hosted không có công cụ thử nghiệm/nhãn không thu tiền.
- POST `/payments/webhook` là route duy nhất. Header X-Payment-Signature chọn hosted raw-body contract.
  Envelope `{data, signature}` không có header chọn local; không xác thực chéo. Body tối đa 16 KiB.
  Local tắt trả 404; hosted tắt trả 503. Invalid → 400, conflict → 409, lỗi DB → 503 retry_later.

## Local callback

`data`: OrderId, Amount, Currency=VND, Provider=local-sandbox, ProviderTransactionId,
Status (Succeeded=1/Failed=2/Cancelled=3), OccurredAtUtc do gateway tạo từ thời điểm Order.
Signature: HMAC-SHA256 hex bằng key ngẫu nhiên 256-bit trong bộ nhớ server, không gửi key/chữ ký tới browser.
Canonical UTF-8 phân cách LF: OrderId N, Amount G29 invariant, Currency, Provider, TransactionId,
Status số, OccurredAtUtc UTC định dạng O; không LF cuối. Field/time/order/provider/quote được kiểm tra.
Header hosted dùng đúng bytes body, không dùng canonical local; xem [hosted contract](../architecture/payment-sandbox.md).

## Order, transaction và lifecycle

Order lưu Provider/ExpiresAtUtc theo migration main. Reuse Order chưa receipt, còn hạn, cùng buyer/course/provider/giá/VND.
Local Failed/Cancelled là terminal; thử lại Order mới. Hosted giữ receipt Failed retry như main.
Price quote không bị sửa khi Course đổi giá; link thanh toán không khả dụng nếu quote không còn khớp giá hiện tại,
nhưng receipt đúng thời điểm/quote vẫn được xác nhận. Order trước metadata migration không được xác nhận mới.

Receipt được lưu kể cả khi Course bị rút hoặc buyer/owner không đủ điều kiện; không cấp Enrollment mới lúc đó.
Không tạo Enrollment trùng hoặc thay quyền Free hợp lệ. Hosted replay không cấp quyền đã từ chối trước đó.
Local legacy receipt thiếu Enrollment được sửa chỉ khi hiện đủ điều kiện. Progress/CompletedAt và readonly Archived của main được giữ.

Shared Serializable transaction khóa commerce, Course, range transaction ID. Payment/Enrollment/Audit commit cùng transaction.
Helper bảo vệ begin/commit/rollback/dispose, giữ lỗi gốc và cancellation. Không tự retry/ACK khi commit chưa rõ;
callback gửi lại đối chiếu database trong transaction mới. Unique violation cũng trả 503 để gửi lại.
Metadata log không gồm exception message/body/secret/signature. Test dùng database GUID riêng, không tắt SQL local.

## Bật local bằng Bash

Giữ Payments__Enabled=false để thử local, không cấu hình/call gateway thật. Connection được kế thừa từ terminal,
không in hoặc đưa credential vào source. Lệnh dành cho người dùng tự chạy, không tự migration/seed:

```bash
ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development \
LearningDemo__Enabled=false Payments__Enabled=false CheckoutSandbox__Enabled=true \
dotnet run --project src/EnglishLearningPlatform.Web --no-launch-profile --urls http://localhost:8081
```

Production/Staging luôn chặn local. Tắt instance rồi unset CheckoutSandbox__Enabled nếu đã export riêng.
Khởi động lại làm key local cũ mất hiệu lực; dữ liệu/Enrollment đã cấp được giữ.
Database phải có migration AddCheckoutOrderMetadata từ main; merge không áp dụng migration đó trên database ứng dụng.

## Kiểm tra browser

Dùng Student2 nếu demo đã có và chưa có quyền Paid; credential local hiện có, không seed.
Mua khóa → xác nhận → Order chờ → Failed/Cancelled/thử lại → Success/Vào học. Refresh/Back không tạo thêm.
Student khác không xem Order; Teacher/Admin bị chặn. Order hosted không có nhãn hoặc nút mô phỏng.
UI 820px, 390px/desktop, Tab/Enter và role alert/status giữ phong cách Estudy.

## Test/review

CheckoutFlowTests + transaction fault tests của nhánh, CheckoutFeatureTests/EnrollmentProgressTests của main
và Free/Learning được chạy trên database test GUID riêng. Chưa gọi provider thật hoặc kiểm thử outage thật/nhiều process.
Không tuyên bố đã pair với Sơn/Khánh. Review tiếp quy tắc quote hết hạn, receipt sau lifecycle, refund và provider thật.

Sau resolve main: build 0 warning/0 error; 111 integration tests và 47 unit tests pass, không skip.
CheckoutMergeTests xác minh hosted không fallback local, local không xác nhận Order hosted, Production chặn local,
receipt khi Course bị rút không cấp quyền, và receipt xảy ra trong hạn được nhận dù đến muộn.
Các test atomicity/replay/concurrency/cancellation/DB failure, hosted checkout, progress và Free được giữ.
Chưa chạy browser end-to-end sau merge hoặc provider thật; UI dùng lại bố cục đã kiểm tra ở 390px/desktop trước merge.
Migration AddCheckoutOrderMetadata là file đã có trên main; không tạo mới hay áp dụng trên database ứng dụng trong phiên resolve.
