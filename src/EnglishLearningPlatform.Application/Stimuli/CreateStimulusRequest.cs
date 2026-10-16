using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Stimuli;

/// <summary>
/// Dữ liệu tạo ngữ liệu; owner và dấu khóa do server quản lý, không nhận từ form.
/// </summary>
public sealed class CreateStimulusRequest
{
    // null nghĩa là chưa chọn. Validator chỉ chấp nhận Passage hoặc Audio.
    public StimulusType? StimulusType { get; set; }

    // Tiêu đề tùy chọn, tối đa 200 ký tự theo mapping hiện tại.
    public string? Title { get; set; }

    // Chỉ dùng cho đoạn đọc Passage.
    public string? ContentText { get; set; }

    // Chỉ dùng cho Audio, tối đa 2048 ký tự.
    public string? ResourceUrl { get; set; }
}
