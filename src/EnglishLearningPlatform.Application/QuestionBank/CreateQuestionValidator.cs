using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Application.Practice;
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
        if (request.PrimarySkill is not EnglishSkill skill || !Enum.IsDefined(skill))
        {
            errors.Add(new QuestionError(nameof(request.PrimarySkill),
            "invalid",
            "Vui lòng chọn một kỹ năng hợp lệ cho câu hỏi."));
        }
        if (request.Level?.Length > 32)
        {
            errors.Add(new(nameof(request.Level)
            , "max_length"
            , "Trình độ không được vượt quá 32 ký tự."));
        }
        if (request.Explanation?.Length > 4000)
            errors.Add(new(nameof(request.Explanation), "max_length", "Giải thích không được vượt quá 4000 ký tự."));

        if (StructuredQuestionEvaluator.IsStructured(request.QuestionType))
        {
            if (!StructuredQuestionEvaluator.IsValidAuthorItems(request.QuestionType, request.StructuredItems))
            {
                string message = request.QuestionType == QuestionType.Matching
                    ? "Nhập từ 2 đến 10 cặp. Mỗi cột phải có nội dung khác nhau, từ 1 đến 200 ký tự."
                    : "Nhập từ 2 đến 10 mục khác nhau, mỗi mục từ 1 đến 200 ký tự, theo thứ tự đúng.";
                errors.Add(new(nameof(request.StructuredItems), "invalid", message));
            }
            if (request.Options is { Count: > 0 } || request.AcceptedAnswers is { Count: > 0 })
                errors.Add(new(nameof(request.StructuredItems), "mixed_answers", "Chỉ nhập đáp án cho loại câu hỏi đã chọn."));
            return errors.AsReadOnly();
        }

        if (request.StructuredItems is { Count: > 0 })
            errors.Add(new(nameof(request.StructuredItems), "mixed_answers", "Loại câu hỏi này không dùng cặp ghép hoặc mục sắp xếp."));
        if (request.QuestionType == QuestionType.Dictation)
        {
            if (request.PrimarySkill != EnglishSkill.Listening)
                errors.Add(new(nameof(request.PrimarySkill), "listening_required", "Câu nghe chép chính tả thuộc kỹ năng Nghe."));
            if (!request.StimulusId.HasValue || request.StimulusId == Guid.Empty)
                errors.Add(new(nameof(request.StimulusId), "audio_required", "Chọn một ngữ liệu âm thanh cho câu nghe chép chính tả."));
        }

        if (request.QuestionType is QuestionType.FillBlank or QuestionType.Dictation)
        {
            if (request.AcceptedAnswers == null || request.AcceptedAnswers.Count == 0)
                errors.Add(new(nameof(request.AcceptedAnswers), "required", "Nhập ít nhất một đáp án được chấp nhận."));
            else
                for (var i = 0; i < request.AcceptedAnswers.Count; i++)
                    if (string.IsNullOrWhiteSpace(request.AcceptedAnswers[i]) || request.AcceptedAnswers[i].Length > 500)
                        errors.Add(new($"AcceptedAnswers[{i}]", "invalid", "Đáp án phải có từ 1 đến 500 ký tự."));
            if (request.Options?.Count > 0)
                errors.Add(new(nameof(request.Options), "invalid", "Câu trả lời bằng văn bản không có lựa chọn trắc nghiệm."));
            return errors.AsReadOnly();
        }
        if (request.QuestionType != QuestionType.MultipleChoice)
        {
            errors.Add(new(nameof(request.QuestionType), "unsupported", "Loại câu hỏi không hợp lệ."));
            return errors.AsReadOnly();
        }
        if (request.AcceptedAnswers?.Count > 0)
            errors.Add(new(nameof(request.AcceptedAnswers), "invalid", "Câu trắc nghiệm sử dụng đáp án trong lựa chọn."));
        if (request.Options is null)
        {
            errors.Add(new(nameof(request.Options)
            , "required"
            , "Danh sách lựa chọn không được để trống."));
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

        for (var index = 0; index < request.Options.Count; index++)
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
