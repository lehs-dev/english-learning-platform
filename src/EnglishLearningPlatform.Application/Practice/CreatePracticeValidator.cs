using System.ComponentModel.DataAnnotations;

namespace EnglishLearningPlatform.Application.Practice;

public sealed class CreatePracticeValidator : ICreatePracticeValidator
{
    public IReadOnlyDictionary<string, string[]> Validate(
        CreatePracticeRequest request)
    {
        // null là lỗi sử dụng hàm, không phải một request hợp lệ.
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var errors = new Dictionary<string, string[]>();

        // Đọc các quy tắc Required và StringLength
        // đã khai báo trên CreatePracticeRequest.
        var validationResults = new List<ValidationResult>();

        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            validationResults,
            validateAllProperties: true);

        foreach (var result in validationResults)
        {
            // Với các attribute hiện tại, MemberNames chứa
            // tên trường bị lỗi, ví dụ Title hoặc Description.
            var field = result.MemberNames.FirstOrDefault()
                ?? string.Empty;

            var message = result.ErrorMessage
                ?? "Dữ liệu không hợp lệ.";

            AddError(errors, field, message);
        }

        // Draft được phép chưa chọn parent.
        // Nhưng tuyệt đối không được có cả hai parent cùng lúc.
        if (request.LessonId.HasValue && request.ModuleId.HasValue)
        {
            const string message =
                "Chỉ được chọn Lesson hoặc Module, không được chọn cả hai.";

            AddError(
                errors,
                nameof(CreatePracticeRequest.LessonId),
                message);

            AddError(
                errors,
                nameof(CreatePracticeRequest.ModuleId),
                message);
        }

        // null nghĩa là chưa chọn.
        // Guid.Empty không phải một ID parent hợp lệ.
        if (request.LessonId.HasValue
            && request.LessonId.Value == Guid.Empty)
        {
            AddError(
                errors,
                nameof(CreatePracticeRequest.LessonId),
                "LessonId không hợp lệ.");
        }

        if (request.ModuleId.HasValue
            && request.ModuleId.Value == Guid.Empty)
        {
            AddError(
                errors,
                nameof(CreatePracticeRequest.ModuleId),
                "ModuleId không hợp lệ.");
        }

        return errors;
    }

    /// <summary>
    /// Thêm lỗi mà không làm mất các lỗi trước đó của cùng trường.
    /// </summary>
    private static void AddError(
        Dictionary<string, string[]> errors,
        string field,
        string message)
    {
        // TryGetValue kiểm tra trường này đã có lỗi hay chưa.
        if (errors.TryGetValue(field, out var existingMessages))
        {
            var messages = new List<string>(existingMessages);
            messages.Add(message);

            errors[field] = messages.ToArray();
        }
        else
        {
            errors[field] = new string[] { message };
        }
    }
}