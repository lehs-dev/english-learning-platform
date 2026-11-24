# Rà soát và lộ trình nâng cấp Estudy

Đánh giá ngày 24/11/2026 trên nhánh `feat/estudy-2.0`, từ baseline `d14ca1f`. Estudy dùng ASP.NET Core MVC, EF Core và SQL Server. Đợt nâng cấp này bổ sung vòng học, làm bài và nhận phản hồi từ giảng viên vào cấu trúc khóa học hiện có.

## Hiện trạng và thay đổi đã triển khai

| Phân hệ | Hiện trạng khi bắt đầu | Thay đổi trong đợt này |
| --- | --- | --- |
| Student | Đã có trang Hôm nay, phòng học theo tab, đề luyện và đề thi, lịch sử kết quả và lộ trình cá nhân. Đường dẫn đến các hoạt động còn phân tán. | Đưa luyện tập và thi lên trang Hôm nay, ưu tiên lượt thi đang diễn ra, mở đúng tab đánh giá khi tới bài cuối khóa và cho phép hoàn thành bài học rồi đi tiếp trong một thao tác. |
| Bài tập và phản hồi | Practice chấm tự động. Teacher xem báo cáo và phân tích theo khóa. Student chưa có nơi nộp bài viết cho Teacher. | Teacher giao đề viết theo khóa và đặt hạn tùy chọn. Student nộp một bài cho mỗi đề và theo dõi phản hồi. Teacher đọc bài, gửi nhận xét và có thể cho điểm trên 100. |
| Tương tác | Hai vai trò sử dụng cùng khóa học, nhưng chưa có trao đổi trực tiếp theo khóa. | Student đã ghi danh mở luồng hỏi đáp riêng. Chỉ người hỏi và Teacher sở hữu khóa học có quyền đọc và trả lời. |
| Streak | Streak dựa trên ngày nhận XP. Một số hoạt động có ý nghĩa có thể chưa đủ tiêu chí nhận XP. | Bảng ngày hoạt động ghi tối đa một dòng mỗi học viên mỗi ngày Việt Nam. Dashboard hiển thị ngày đã học và trạng thái hôm nay. Dữ liệu lịch sử được khôi phục từ các hoạt động còn lưu. |
| Teacher | Đã có trình quản lý khóa, chương, bài học, ngân hàng câu hỏi, đề luyện và vòng đời đề thi. Học liệu bài học chủ yếu nhập URL. Thẻ được thêm riêng lẻ. | Tải ảnh và audio trực tiếp vào bài học. Nhập nhiều flashcard từ CSV. Thêm đường dẫn soạn bài luyện, đề thi, bộ thẻ, bài viết và hỏi đáp từ ngữ cảnh khóa học hoặc bài học phù hợp. |
| Hình thức làm bài | Đã có trắc nghiệm, điền từ, ghép cặp và sắp xếp. Đề thi lưu đáp án trên máy chủ và chốt theo hạn. | Thêm câu nghe chép chính tả dùng được cho luyện tập và thi. Teacher gắn audio và đáp án hợp lệ. Đề bài chỉ hiện nội dung cần để làm bài, còn đáp án và giải thích đến sau khi chấm theo quy tắc từng chế độ. Kết quả bài luyện lọc được câu sai hoặc bỏ trống. Bài viết dùng luồng nhận xét riêng. |
| Database và backend | Đã có khóa ngoại, ràng buộc ghi danh, bản lưu kết quả luyện tập và khóa nội dung được dùng trong đề. | Migration bổ sung bài viết, hỏi đáp, sổ ngày học và loại câu hỏi mới. Chỉ mục và ràng buộc bảo vệ mỗi lượt nộp bài viết, thứ tự phản hồi và ngày học duy nhất. |
| Admin và thanh toán | Đã có quản trị tài khoản, báo cáo doanh thu và tra cứu thanh toán. Bộ tích hợp VNPay trong repo dùng sandbox. | Đưa quản trị học liệu và các yêu cầu vận hành thanh toán công khai vào lộ trình tiếp theo. |

### Quy tắc sử dụng

- Một ngày học hợp lệ được ghi khi học viên đánh dấu hoàn thành bài học, nộp bài luyện hoặc bài viết, ôn flashcard, hoặc hoàn tất bài thi có câu trả lời. Mốc ngày là 00 giờ theo UTC+7. Bài thi chốt tự động tính theo ngày của câu trả lời hợp lệ lưu gần nhất. XP giữ quy tắc thưởng hiện hữu.
- Bài viết được giao cho khóa học. Khóa đang soạn có thể chuẩn bị đề trước. Học viên đã ghi danh xem đề khi khóa có quyền truy cập. Hạn nộp được lưu theo UTC và hiển thị theo giờ Việt Nam.
- Câu nghe chép chính tả dùng đáp án văn bản được khai báo trước. Hệ thống chuẩn hóa khoảng trắng và chữ hoa, chữ thường. Dấu câu vẫn được so khớp. Teacher có thể khai báo nhiều biến thể được chấp nhận.
- Ảnh và audio bài học lưu trong `App_Data/lesson-media`. Docker Compose có volume riêng cho thư mục này. Có thể đặt đường dẫn bằng `LessonMedia__StoragePath`. Endpoint học liệu dùng quyền truy cập của tài nguyên bài học. Tệp đã thay thế hoặc gỡ khỏi bài học cần được dọn khỏi kho lưu trữ trong đợt vận hành tiếp theo.

## Cơ chế tham chiếu

- Duolingo tách streak khỏi mục tiêu dài hơn và cho phép một hoạt động học hoàn tất duy trì chuỗi ngày. Estudy áp dụng nguyên tắc này cho các hoạt động học đã được xác nhận phía máy chủ. [Duolingo về streak](https://blog.duolingo.com/improving-the-streak/)
- STUDY4 hướng dẫn học viên quay lại câu sai, đọc giải thích và dùng transcript để ôn. Estudy thêm bộ lọc câu sai trong kết quả luyện tập và đưa hàng ôn tập riêng, transcript theo đoạn vào giai đoạn kế tiếp. [STUDY4 về luyện và chữa IELTS Listening](https://study4.com/posts/4451/huong-dan-luyen-de-ielts-listening-tren-study4-hieu-qua/)
- H5P dùng audio và bản chép lời cho bài dictation. British Council tổ chức viết theo CEFR, thể loại và mẫu bài. Estudy bổ sung dictation và cho Teacher đưa yêu cầu, tiêu chí hoặc ví dụ vào đề viết. [H5P Dictation](https://h5p.org/dictation) · [British Council LearnEnglish Writing](https://learnenglish.britishcouncil.org/free-resources/writing)

## Ưu tiên tiếp theo

| Ưu tiên | Database và backend | UI/UX và chức năng | Điều kiện nghiệm thu |
| --- | --- | --- | --- |
| P0 · Đưa bản nâng cấp vào môi trường sử dụng | Diễn tập sao lưu và phục hồi SQL Server cùng thư mục học liệu. Áp dụng migration trên bản sao dữ liệu, kiểm tra số dòng backfill và giám sát thời gian, dung lượng log. | Duyệt các luồng Teacher và Student bằng hai tài khoản ở màn hình desktop và di động. Kiểm tra ảnh, audio, CSV, bài luyện và thi ở mạng chậm. | Đề cũ và lượt thi đang làm mở đúng. Dòng ngày học không trùng. Quyền khóa học áp dụng đúng cho học liệu, bài nộp và hỏi đáp. |
| P1 · Củng cố lớp học | Bổ sung trạng thái bản nháp và lịch xuất bản cho bài viết, phiên bản đề và lần nộp, rubric và thay đổi điểm có lịch sử. Xây dựng thông báo trong ứng dụng theo sự kiện đã lưu. | Hộp việc cần làm, hạn nộp và phản hồi mới tại trang Hôm nay. Hàng chờ chấm bài và hỏi đáp ở Teacher. Luồng tạo theo bước với xem trước trước khi giao hoặc xuất bản. | Giáo viên thao tác từ một nơi. Học viên tìm được việc cần làm, lưu nháp và thấy phản hồi. Thông báo không gửi trùng. |
| P1 · Mở rộng luyện tập | Lưu mục ôn tập theo câu sai và phiên bản đề. Gắn transcript theo đoạn và quy định thời điểm được xem. Dùng lại ngân hàng câu hỏi và cơ chế ôn flashcard. | Làm lại câu sai, luyện theo kỹ năng, luyện nghe theo đoạn, điền nhiều chỗ trống và bài sửa lỗi. Thêm hướng dẫn bàn phím, trạng thái lưu và bố cục ngữ liệu cố định khi làm đề dài. | Đáp án và transcript chỉ xuất hiện đúng thời điểm. Ôn lại bám theo lịch sử của học viên. Các dạng bài dùng được trên bàn phím và thiết bị cảm ứng. |
| P2 · Luyện nói và học liệu quy mô lớn | Lưu bản ghi âm riêng tư với quyền truy cập, thời hạn lưu và lịch sử nhận xét. Chuyển học liệu sang kho đối tượng nếu chạy nhiều máy chủ. Bổ sung giới hạn dung lượng, xử lý tệp và đo lượng sử dụng. | Ghi âm hoặc nộp audio để Teacher góp ý về phát âm, độ trôi chảy và hoàn thành yêu cầu. Thư viện học liệu có tái sử dụng và xem trước. Admin quản lý báo cáo và quota. | Chỉ đúng người mở được bản ghi. Sự cố lưu tệp có hướng phục hồi. Âm thanh phát được từ các máy chủ web đã triển khai. |
| P2 · Vận hành và phân tích | Theo dõi truy vấn chậm, kiểm tra chỉ mục từ workload thực tế, đồng bộ đo lường app và SQL. Hoàn thiện quy trình đối soát, hoàn tiền và phục hồi theo môi trường thanh toán. | Báo cáo Student và Teacher theo kỹ năng, lớp, nhóm câu sai và mốc tiến độ; thiết lập nhắc học khi học viên đồng ý. | Báo cáo khớp dữ liệu nguồn. Hệ thống có ngưỡng cảnh báo và bản sao lưu đã thử phục hồi. |

## Áp dụng và kiểm tra

Migration `AddCourseCollaborationAndActivity` là phần đi cùng bản app. Chạy bước migrate theo quy trình đã có trong README hoặc Docker Compose. Migration từ chối hạ phiên bản khi có dữ liệu cần schema mới. Khi cần phục hồi một môi trường có dữ liệu mới, dùng bản sao lưu SQL Server và thư mục học liệu tương ứng.

Dùng `tests/low-memory.runsettings`, một tiến trình build và bộ lọc theo lớp hoặc ca kiểm thử. Kiểm tra ưu tiên gồm nâng cấp từ schema cũ, streak qua ngày, chấm và bảo vệ đáp án nghe chép, quyền hỏi đáp, nộp và nhận xét bài viết, cùng tải học liệu và nhập CSV.

Kết quả xác minh gồm 15 ca tích hợp và 10 ca đơn vị theo bộ lọc. Các ca tích hợp dùng database tạm trên SQL Server devcontainer. Một lượt Chromium kiểm tra hỏi đáp và nộp bài viết ở viewport 390 px, giao bài và tạo dạng nghe chép ở viewport 1280 px. Việc áp dụng migration và diễn tập khôi phục trên bản sao dữ liệu vận hành nằm trong P0.
