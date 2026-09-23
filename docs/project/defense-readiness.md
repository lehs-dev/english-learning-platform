# Chuẩn bị demo và vấn đáp

Theo kế hoạch môn Công nghệ hiện đại, **Sơn, Khánh, Hoàng, Dương** đều có thể bị hỏi ngẫu nhiên ở tuần 7. Khang phụ trách nhiều phần UI/Admin/tài liệu, cũng cần tự demo và giải thích phần đã làm ở bản nộp tuần 13. Ghi nhận đóng góp thật bằng issue, commit, PR, test và báo cáo; người cover phải chạy được phần của người mình cover.

## Trước tuần 7, mỗi người tự làm được

1. Clone/config/build/test/run từ máy sạch; giải thích `.env`, connection string, migration và cách đọc lỗi setup. Trước khi có migration được merge, nói rõ giới hạn của skeleton hiện tại.
2. Vẽ lại ranh giới bốn project và ERD **đích** `Course → Module → Lesson`, Question Bank → Practice/Assessment → Attempt/Result. Chỉ ra khác biệt giữa thiết kế đích và code đang chạy, không trình bày tính năng chưa có là đã hoàn thành.
3. Trace HTTP request qua middleware → authentication/authorization → controller → Application service → EF/SQL Server → response. Giải thích DI lifetime, `DbContext` scoped và AuthN trước AuthZ.
4. Trace một luồng Learning và một luồng Assessment, gồm owner/role, dữ liệu lưu, điểm/timer và trường hợp request lặp. Phân biệt Practice (feedback, kết quả gần nhất) với Assessment (attempt, deadline, history).
5. Tự thêm validation/endpoint nhỏ, chạy test, đọc CI failure, sửa một bug ngoài module chính và giải một Git conflict trên nhánh thử nghiệm.

## Cách luyện mỗi tuần

| Thời điểm | Bài luyện và bằng chứng |
| --- | --- |
| Tuần 1–2 | Mỗi người setup, giải thích scope/actor, vẽ request pipeline; review ERD/Identity policy. |
| Tuần 3–4 | Đổi người trace Course/Enrollment/Progress và Question/Practice; test 401/403/owner và một lỗi EF nhỏ. |
| Tuần 5–6 | Đổi người trace Start → save → Resume → submit/timeout → scoring; bug drill concurrency và điểm skill; lưu log test/PR. |
| Tuần 7 | Bốc ngẫu nhiên người và module; demo từ máy sạch, giải thích code, sửa bug trong thời gian giới hạn, chốt bằng chứng môn học. |
| Tuần 8–13 | Duy trì luyện Final, Admin, audit, lifecycle và luồng bốn actor cho bản nộp .NET/QLDA. |

Cuối mỗi tuần tổ chức **bug drill khoảng 30 phút**: người không phải owner nhận tình huống, tự mô tả cách tái hiện, tìm code, sửa/test và giải thích. Ghi issue/PR hoặc biên bản ngắn gồm người thử, kết quả, lỗi còn tồn tại. Không đánh dấu “đã biết” nếu chỉ xem owner thao tác.

## Câu hỏi nhanh để tự kiểm tra

- Teacher A có sửa Course/Question của Teacher B được không? Chặn ở tầng nào và test ra sao?
- Vì sao Lesson có thể mở không tuần tự? Progress xử lý mẫu số 0 và `CompletedAt` lịch sử thế nào?
- Practice làm lại khác Assessment retake ở kết quả lưu, feedback, điểm và deadline như thế nào?
- Hai request Start/Submit đồng thời có làm vượt `MaxAttempts` hoặc chấm hai lần không?
- Vì sao Placement không trả chứng nhận CEFR/TOEIC? Skill breakdown chỉ hiển thị khi nào?
- CI đang chạy loại test nào? CD đưa image đi đâu và vì sao chưa phải deploy website?
