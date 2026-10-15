using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.QuestionBank;

// Quy định chức năng kiểm tra một câu hỏi trước khi dùng cho nội dung mới.
// Interface chỉ khai báo hàm; class xử lý sẽ được viết ở bước tiếp theo.
public interface IQuestionUsageValidator
{
    // ownerTeacherUserId:
    // ID của Teacher sở hữu Practice/Assessment đang được soạn.
    // Phía server phải xác định ID này, không tin ID do người dùng nhập.
    //
    // question:
    // Câu hỏi đã đọc từ database.
    // Phía gọi phải tải cả Options và Stimulus trước khi kiểm tra.
    // Cho phép null để xử lý trường hợp không tìm thấy câu hỏi.
    //
    // targetSkill:
    // Skill bắt buộc của bài nếu đây là Skill Assessment.
    // Ví dụ: bài Reading chỉ được dùng câu hỏi PrimarySkill = Reading.
    // Với Practice hoặc bài không giới hạn một skill, truyền null.
    //
    // Kết quả:
    // Danh sách rỗng nghĩa là câu hỏi hợp lệ.
    // Danh sách có phần tử nghĩa là có lỗi cần hiển thị hoặc từ chối.
    //
    // Chỉ dùng cho việc đưa câu hỏi vào nội dung mới.
    // Không dùng hàm này để chặn xem kết quả lịch sử của câu hỏi Archived.
    IReadOnlyList<QuestionError> ValidateForNewUse(
        Guid ownerTeacherUserId,
        Question? question,
        EnglishSkill? targetSkill = null);
            // Kiểm tra câu hỏi đã được gắn vào Practice/Assessment có sẵn.
    // Vẫn kiểm tra ownership, MCQ, skill và Stimulus.
    // Không từ chối chỉ vì câu hỏi đã chuyển sang Archived.
    //
    // Phía gọi phải tải câu hỏi từ liên kết PracticeQuestion/
    // AssessmentQuestion có thật, không dùng hàm này để chọn câu hỏi mới.
    //
    // Không dùng để chặn việc đọc kết quả lịch sử đã chấm.
    IReadOnlyList<QuestionError> ValidateForExistingUse(
        Guid ownerTeacherUserId,
        Question? question,
        EnglishSkill? targetSkill = null);
}