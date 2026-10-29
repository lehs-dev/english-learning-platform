# Sửa câu hỏi của bài luyện tập đang mở

Theo yêu cầu cập nhật, Practice được thêm/bỏ/đổi thứ tự câu hỏi ngay khi Published,
không cần ngừng xuất bản hoặc tạo Practice khác. Quy tắc của Assessment không thay đổi.

Trên giao diện, `Published` hiển thị là **Đang mở**, `Unpublished` là **Tạm đóng**.
Hai nút thao tác có tên **Mở bài luyện tập** và **Tạm đóng bài luyện tập**.

| Trạng thái học viên | Khi Teacher lưu đề mới |
| --- | --- |
| Chưa mở bài | Nhận phiên bản mới nhất |
| Đang làm, chưa nộp | Giữ nguyên các câu hỏi và thứ tự của phiên bản đã mở |
| Đã nộp | Giữ điểm, đáp án và giải thích của kết quả đã nộp |
| Làm lại sau khi nộp | Nhận phiên bản mới nhất; khi nộp sẽ thay kết quả gần nhất theo quy tắc Practice |

## Dữ liệu và kiểm tra

- `Practice.Version` xác định đề mới nhất. Draft vẫn sửa trực tiếp; mỗi lần lưu bài
  Published/Unpublished tạo phiên bản tiếp theo và giữ lại liên kết câu hỏi cũ.
- `PracticeQuestion.Version` giữ danh sách/thứ tự của từng phiên bản.
- `PracticeSession` lưu phiên bản khi học viên mở trang làm bài; tải lại trang giữ
  cùng phiên. Đây không phải Assessment Attempt và không giới hạn số lần luyện tập.
- `PracticeSubmission.Version` xác định đề dùng để hiển thị kết quả gần nhất.
- Session thuộc đúng Student và Practice; gửi lại một session đã nộp không được
  ghi đè kết quả của lần làm lại sau đó. Trang Result không tự tạo phiên mới.
- Course và Practice được khóa theo cùng thứ tự khi mở/lưu/nộp để tránh sửa chen
  giữa một lần chấm. Tab Teacher cũ bị từ chối nếu UpdatedAtUtc đã thay đổi.
- Câu hỏi mới phải cùng owner, MCQ hợp lệ; Listening phải có audio hợp lệ.
  Published/Unpublished không được lưu đề rỗng. Parent vẫn giữ nguyên sau Publish.
- Nội dung Question/Option/Stimulus đã sử dụng vẫn bất biến để bảo toàn đề cũ và
  nội dung dùng chung với Assessment. Muốn đổi lời câu hỏi/đáp án, tạo câu hỏi mới
  trong ngân hàng rồi thay vào Practice; chức năng này không sửa đè câu hỏi cũ.
- Chưa bổ sung lưu nháp lựa chọn của Practice: tải lại giữ phiên bản đề, nhưng
  không tự khôi phục các nút đáp án chưa nộp. Unpublish vẫn chặn mở/nộp như trước.

## Migration

Migration: `20261028171139_AddPracticeVersionsAndSessions`.
Các bản ghi hiện có nhận Version = 1; không xóa submission, answer hoặc câu hỏi.
Phiên làm bài mở trước khi triển khai chưa có SessionId: nộp form còn đúng danh sách
hiện hành thì được xử lý theo luồng cũ; nếu đề đã đổi, form cũ bị từ chối và cần mở lại.

Cập nhật database bằng cấu hình kết nối đúng môi trường trước khi chạy web mới:

```powershell
dotnet ef database update --project src/EnglishLearningPlatform.Infrastructure --startup-project src/EnglishLearningPlatform.Web
```

Với web Docker, chạy lần lượt để build image mới, áp dụng migration rồi mở lại web:

```powershell
docker compose build web migrate
docker compose run --rm migrate
docker compose up -d --no-deps web
```

Sao lưu database trước khi triển khai. Không downgrade migration sau khi đã phát
sinh nhiều phiên bản: unique key cũ chỉ cho một bản mỗi Question trong Practice.

## Kiểm tra giao diện

1. Teacher dùng **Mở bài luyện tập** cho đề có hai câu A, B. Cho hai Student enroll cùng Course.
2. Student thứ nhất mở bài rồi giữ tab, chưa nộp.
3. Teacher mở **Bài luyện tập → Mở bài**, bỏ A, thêm C, đổi thứ tự rồi lưu.
   Màn hình hiển thị phiên bản mới; Xem trước chỉ có đề mới.
4. Student thứ hai mới mở bài: thấy đề mới. Student thứ nhất F5: vẫn thấy A, B;
   nộp bài được chấm theo A, B.
5. Teacher sửa thêm lần nữa: trang kết quả Student thứ nhất không đổi.
6. Student thứ nhất bấm làm lại: nhận đề mới nhất. Nộp rồi gửi lại form cũ không
   làm thay đổi kết quả của lượt mới.

Test tự động nằm trong `TeacherPracticeQuestionsTests`: vòng đời phiên bản,
form HTTP thật, quyền SessionId, tab Teacher cũ, lưu sai và thao tác đồng thời.
