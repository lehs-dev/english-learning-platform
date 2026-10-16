using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.Stimuli;

public sealed class CreateStimulusValidator : ICreateStimulusValidator
{
    /// <summary>
    /// Kiểm tra loại, độ dài và dữ liệu tương ứng; không thay đổi request đầu vào.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Validate(CreateStimulusRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var errors = new Dictionary<string, string[]>();

        if (request.Title != null && request.Title.Length > 200)
        {
            errors[nameof(request.Title)] = new[] { "Tiêu đề không được vượt quá 200 ký tự." };
        }

        if (!request.StimulusType.HasValue
            || !Enum.IsDefined(typeof(StimulusType), request.StimulusType.Value))
        {
            errors[nameof(request.StimulusType)] = new[] { "Bạn phải chọn loại Đoạn đọc hoặc Âm thanh." };
            return errors;
        }

        if (request.StimulusType == StimulusType.Passage)
        {
            if (string.IsNullOrWhiteSpace(request.ContentText))
            {
                errors[nameof(request.ContentText)] = new[] { "Đoạn đọc phải có nội dung." };
            }

            // Không âm thầm bỏ dữ liệu gửi nhầm sang loại ngữ liệu khác.
            if (!string.IsNullOrWhiteSpace(request.ResourceUrl))
            {
                errors[nameof(request.ResourceUrl)] = new[] { "Đoạn đọc không dùng URL âm thanh. Hãy để trống trường này." };
            }
        }
        else
        {
            // Sau kiểm tra enum ở trên, nhánh còn lại chỉ có thể là Audio.
            if (!string.IsNullOrWhiteSpace(request.ContentText))
            {
                errors[nameof(request.ContentText)] = new[] { "Âm thanh không dùng nội dung đoạn đọc. Hãy để trống trường này." };
            }

            if (string.IsNullOrWhiteSpace(request.ResourceUrl))
            {
                errors[nameof(request.ResourceUrl)] = new[] { "Âm thanh phải có URL tài nguyên." };
            }
            else if (request.ResourceUrl.Length > 2048)
            {
                errors[nameof(request.ResourceUrl)] = new[] { "URL âm thanh không được vượt quá 2048 ký tự." };
            }
            else if (!CourseRules.ValidResource(
                LessonResourceType.Audio, null, request.ResourceUrl.Trim()))
            {
                // Dùng lại rule HTTP/HTTPS của dự án, không suy đoán loại file qua phần đuôi.
                errors[nameof(request.ResourceUrl)] = new[] { "URL âm thanh phải là địa chỉ HTTP/HTTPS tuyệt đối hợp lệ." };
            }
        }

        return errors;
    }
}
