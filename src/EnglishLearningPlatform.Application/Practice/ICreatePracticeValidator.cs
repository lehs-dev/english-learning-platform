namespace EnglishLearningPlatform.Application.Practice;

/// <summary>
/// Kiểm tra dữ liệu đầu vào khi tạo Practice bản nháp.
/// Không truy cập database và không kiểm tra quyền tài khoản.
/// </summary>
public interface ICreatePracticeValidator
{
    // Key là tên trường bị lỗi.
    // Value là các thông báo lỗi của trường đó.
    // Không có lỗi thì trả về dictionary rỗng.
    IReadOnlyDictionary<string, string[]> Validate(
        CreatePracticeRequest request);
}