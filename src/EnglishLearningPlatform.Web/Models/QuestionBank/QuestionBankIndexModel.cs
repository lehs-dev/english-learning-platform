using EnglishLearningPlatform.Application.QuestionBank;

namespace EnglishLearningPlatform.Web.Models.QuestionBank;

// Dữ liệu dành cho trang danh sách Question Bank.
// Đây không phải entity database và không tạo thêm bảng.
public sealed class QuestionBankIndexModel
{
    // Bộ lọc Teacher đã nhập.
    // View dùng để giữ lại từ khóa, skill, level... sau khi tìm kiếm.
    public QuestionSearchRequest Query { get; }

    // Kết quả tìm kiếm: danh sách câu hỏi, tổng số câu và thông tin trang.
    public QuestionSearchResult Result { get; }

    // Khi tạo model, phải cung cấp cả bộ lọc và kết quả.
    // Không để view nhận dữ liệu null rồi lỗi khi hiển thị.
    public QuestionBankIndexModel(
        QuestionSearchRequest query,
        QuestionSearchResult result)
    {
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        Query = query;
        Result = result;
    }
}