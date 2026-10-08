namespace EnglishLearningPlatform.Application.Assessment;

public enum AttemptStartStatus
{
    Started,
    Resumed,
    NeedsFinalization, // NeedsFinalization có nghĩa là Attempt cũ đã hết hạn
    //  và phải được chấm trước khi có thể bắt đầu lượt tiếp theo. 
    // Không được xóa Attempt hết hạn hoặc tự cấp thêm lượt làm.
    LimitReached,
    Forbidden,
    NotFound,
    Unavailable,
    ExpiredFinalized
}
