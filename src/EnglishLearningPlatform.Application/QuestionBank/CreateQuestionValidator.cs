using EnglishLearningPlatform.Domain.Enums;
namespace EnglishLearningPlatform.Application.QuestionBank;
public sealed class CreateQuestionValidator : ICreateQuestionValidator
{
    public IReadOnlyList<QuestionError> Validate(CreateQuestionRequest request)
    {
         ArgumentNullException.ThrowIfNull(request);
         var errors = new List<QuestionError>();
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            errors.Add(new QuestionError(
                nameof(request.Content),
                "required",
                "Nội dung câu hỏi không được để trống."));
        }
        else if (request.Content.Length > 4000)
        {
            errors.Add(new QuestionError(
                nameof(request.Content),
                "max_length",
                "Nội dung câu hỏi không được vượt quá 4000 ký tự."));
        }
        if(request.PrimarySkill is not EnglishSkill skill || !Enum.IsDefined(skill))
        {
            errors.Add(new QuestionError(nameof(request.PrimarySkill),
            "invalid",
            "Câu hỏi phải có một PrimarySkill hợp lệ."));
        }
        if(request.Level?.Length > 32)
        {
            errors.Add(new (nameof(request.Level)
            ,"max_length"
            ,"Level Khong Duoc Vuot Qua 32 Ky Tu"));
        }
        if(request.Difficulty?.Length > 32)
        {
            errors.Add(new(
                nameof(request.Difficulty),
                "max_length",
                "Difficulty không được vượt quá 32 ký tự."));
        }
        if(request.Options is null)
        {
            errors.Add(new(nameof(request.Options)
            ,"required"
            ,"Danh Sach Lua Chon Khong Duoc De Trong"));
            return errors.AsReadOnly();
        }
        if (request.Options.Count < 2)
        {
            errors.Add(new(
                nameof(request.Options),
                "min_count",
                "Câu hỏi phải có ít nhất hai lựa chọn."));
        }

        var correctCount = 0;

        for(var index = 0; index < request.Options.Count; index++)
        {
            var option = request.Options[index];
            var field = $"Options[{index}]";
            if (option is null)
            {
                errors.Add(new(
                    field,
                    "required",
                    "Lựa chọn không được null."));

                continue;
            }
            if (string.IsNullOrWhiteSpace(option.Content))
            {
                errors.Add(new(
                    $"{field}.Content",
                    "required",
                    "Nội dung lựa chọn không được để trống."));
            }
            else if (option.Content.Length > 1000)
            {
                errors.Add(new(
                    $"{field}.Content",
                    "max_length",
                    "Nội dung lựa chọn không được vượt quá 1000 ký tự."));
            }
            if (option.IsCorrect)
            {
                correctCount++;
            }
        }
        if (correctCount != 1)
        {
            errors.Add(new(
                nameof(request.Options),
                "exactly_one_correct",
                "Câu hỏi phải có đúng một đáp án đúng."));
        }

        return errors.AsReadOnly();
    }
}