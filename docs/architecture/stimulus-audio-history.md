# Nội dung lịch sử của Question, Stimulus và Assessment

## Question và Stimulus

- Từ lần Publish/Attempt đầu tiên, giữ nguyên nội dung Question, PrimarySkill,
  StimulusId, giải thích và tập Option (text, đúng/sai, thứ tự/ID).
- Dấu `ContentLockedAtUtc` là một chiều. Unpublish, bỏ câu khỏi đề, Clone hoặc
  Archive không xóa dấu này. Level vẫn là metadata được sửa.
- `QuestionAuthoringService` trả lỗi theo trường; `ContentImmutabilityGuard` chặn
  thêm ở cả `SaveChanges` và `SaveChangesAsync`, kiểm tra dữ liệu đang lưu thật.
  Stimulus dùng chung không được sửa chỉ vì có một Question Draft khác tham chiếu.
- Không có HTTP API sửa/xóa Stimulus. Muốn thay passage/audio đã dùng, tạo
  Stimulus mới và Question mới. Archive giữ nội dung/Option cho lịch sử.
- Đây không phải hàng rào chống quản trị viên database. Migration, raw SQL,
  `ExecuteUpdate/Delete` và quyền sửa trực tiếp database phải được kiểm soát riêng;
  không dùng chúng trong các endpoint chỉnh nội dung để vượt qua guard.

## Audio bất biến do ứng dụng quản lý

Teacher upload MP3/WAV/Ogg tối đa 10 MiB tại **Tạo ngữ liệu đọc/nghe**. Ứng dụng
kiểm tra dung lượng/chữ ký định dạng, sinh URL `/StimulusAudio/Play/{sha256}`
từ bytes và lưu ngoài `wwwroot`. Không dùng tên file do browser gửi làm đường dẫn.
Kiểm tra chữ ký không thay thế bộ giải mã âm thanh hoặc phần mềm quét mã độc.

- Không có API overwrite/delete. Bytes khác có hash/URL khác; bytes giống nhau
  dùng lại file sau khi kiểm tra tính toàn vẹn.
- Mỗi lần phát đọc một bản bytes có giới hạn, tính lại SHA-256 rồi mới trả nội dung.
  File bị sửa/hỏng bị từ chối, không phát bytes mới dưới URL cũ. Có hỗ trợ HTTP Range.
- Hash không thay thế authorization. Chỉ owner Teacher, Student có quyền theo
  Assessment/Course/Enrollment hợp lệ hoặc lịch sử bài làm của chính mình được đọc.
  Enrollment có Payment chưa Succeeded không mở quyền đọc audio nội bộ.
- Publish mới của Practice/Assessment yêu cầu audio nội bộ. Thêm câu audio mới
  vào Assessment Published cũng áp dụng quy tắc này. File nội bộ thiếu/hỏng chặn
  Publish và thay định nghĩa Published; không khóa một phần nội dung trước khi lỗi.

### URL bên ngoài và dữ liệu cũ

URL HTTP/HTTPS ngoài chỉ được kiểm tra định dạng, không được server tự tải về
(tránh biến trường nhập URL thành chức năng truy cập mạng nội bộ/SSRF).
Không thể ngăn nhà cung cấp thay/xóa bytes tại cùng URL và không thể khôi phục
chính xác audio Student đã nghe trong quá khứ nếu trước đây chưa lưu snapshot.

Vì vậy giữ nguyên các đề lịch sử dùng URL ngoài và hiển thị cảnh báo ở Detail,
Teacher Preview và màn hình làm bài. Không âm thầm chuyển URL cũ sang một bản
download mới hoặc sửa Stimulus đã khóa. Đề mới phải dùng file upload; nếu cần
thay audio cũ thì tạo Stimulus/Question mới và phiên bản Assessment mới.

### Vận hành và sao lưu

- Mặc định: `<ContentRoot>/App_Data/stimulus-audio`. Có thể cấu hình đường dẫn
  bằng `StimulusAudio__StoragePath`; tài khoản chạy web cần quyền đọc/ghi.
- Docker Compose mount named volume `stimulus-audio-data`; rebuild container
  không làm mất file. **Không dùng `docker compose down -v` để cập nhật code.**
- Backup/restore database và kho audio cùng nhau. Hash chỉ phát hiện thay đổi,
  không phục hồi file đã mất và không thay thế backup/quyền filesystem.
- MVP dùng một kho local/persistent volume. Nếu chạy nhiều web replica/máy,
  cần một kho dùng chung bất biến; không để mỗi replica có thư mục riêng.
- File đã ghi nhưng transaction DB thất bại có thể trở thành file mồ côi.
  Chưa có GC tự động; không xóa file đang được tham chiếu hoặc còn cần cho lịch sử.
  Theo dõi dung lượng/quota và chỉ dọn offline sau khi đối chiếu dữ liệu/backup.

## Clone Assessment

**Bài kiểm tra → Clone thành Draft** tạo Assessment và AssessmentQuestion có Id
mới, sao chép loại/TargetSkill, cấu hình, CourseId, QuestionId, thứ tự và points.
Không sao chép Attempt, Answer hoặc SkillResult, không thay đổi bản gốc và không
thay `Course.FinalAssessmentId`. Teacher khác không được Clone đề của owner.

Clone dùng lại Question/Stimulus hiện có: chúng vẫn khóa. Muốn sửa nội dung phải
thay bằng Question/Stimulus mới. Nếu clone giữ Course đã Archived, dùng chức năng
Phạm vi/Course để chuyển hợp lệ trước khi Publish. Question Archived có thể nằm
trong bản Draft sao chép nhưng phải được thay trước khi Publish như nội dung mới.

Từ Attempt đầu tiên, các endpoint cấu hình/câu hỏi/scope của bản gốc từ chối sửa,
kể cả request trực tiếp hoặc tab cũ. Final đã có Attempt không được thay/gỡ;
Publish bản clone không mở khóa Final cũ. Lỗi đề Final phát hiện sau Attempt đầu
tiên vẫn là giới hạn vận hành của MVP, không có cơ chế sửa điểm/thay Final hồi tố.
