namespace EnglishLearningPlatform.Application.Stimuli;

/// <summary>
/// Kiểm tra dữ liệu thuần túy, không truy cập database hoặc tải URL.
/// </summary>
public interface ICreateStimulusValidator
{
    // Key là tên trường; dictionary rỗng nghĩa là không có lỗi.
    IReadOnlyDictionary<string, string[]> Validate(CreateStimulusRequest request);
}
