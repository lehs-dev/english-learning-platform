using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Application.QuestionBank;

public sealed class QuestionUsageValidator : IQuestionUsageValidator
{
     private readonly ICreateQuestionValidator _createQuestionValidator;
     // Nhận validator đã có qua DI.
    // Tận dụng các rule MCQ hiện tại, không viết lại cùng một logic.
    public QuestionUsageValidator(
        ICreateQuestionValidator createQuestionValidator)
    {
        if (createQuestionValidator == null)
        {
            throw new ArgumentNullException(
                nameof(createQuestionValidator));
        }

        _createQuestionValidator = createQuestionValidator;
    }
        // Dùng khi Teacher chọn câu hỏi cho nội dung mới.
    // Archived sẽ bị từ chối.
    public IReadOnlyList<QuestionError> ValidateForNewUse(
        Guid ownerTeacherUserId,
        Question? question,
        EnglishSkill? targetSkill = null)
    {
        return ValidateCore(
            ownerTeacherUserId,
            question,
            targetSkill,
            true);
    }

    // Dùng để kiểm tra câu hỏi đã thuộc một bài có sẵn.
    // Archived vẫn được phép nếu những điều kiện khác hợp lệ.
    public IReadOnlyList<QuestionError> ValidateForExistingUse(
        Guid ownerTeacherUserId,
        Question? question,
        EnglishSkill? targetSkill = null)
    {
        return ValidateCore(
            ownerTeacherUserId,
            question,
            targetSkill,
            false);
    }
    // Xử lý chung cho cả việc chọn câu hỏi mới và sử dụng bài có sẵn.
    // isNewUse chỉ quyết định có từ chối trạng thái Archived hay không.
    private IReadOnlyList<QuestionError> ValidateCore(
        Guid ownerTeacherUserId,
        Question? question,
        EnglishSkill? targetSkill,
        bool isNewUse)
    {
        List<QuestionError> errors = new List<QuestionError>();

        // 1. Owner phải được phía gọi xác định trước khi kiểm tra.
        // Hàm này không thay thế việc kiểm tra tài khoản/role Teacher.
        if (ownerTeacherUserId == Guid.Empty)
        {
            errors.Add(new QuestionError(
                nameof(ownerTeacherUserId),
                "invalid_owner",
                "Không xác định được Teacher sở hữu bài."));

            return errors.AsReadOnly();
        }
        // 2. Không tìm thấy câu hỏi thì không được sử dụng.
        if (question == null)
        {
            errors.Add(new QuestionError(
                "QuestionId",
                "not_found",
                "Không tìm thấy câu hỏi hợp lệ."));

            return errors.AsReadOnly();
        }
        // 3. Không cho sử dụng câu hỏi của Teacher khác.
        // Trả về ngay, không kiểm tra rồi tiết lộ thêm dữ liệu của họ.
        if (question.OwnerTeacherUserId != ownerTeacherUserId)
        {
            errors.Add(new QuestionError(
                "QuestionId",
                "forbidden",
                "Bạn không có quyền sử dụng câu hỏi này."));

            return errors.AsReadOnly();
        }
        // 4. Trạng thái phải thuộc enum được định nghĩa.
        if (!Enum.IsDefined(typeof(QuestionStatus), question.Status))
        {
            errors.Add(new QuestionError(
                nameof(Question.Status),
                "invalid",
                "Trạng thái câu hỏi không hợp lệ."));
        }
                // Chỉ chặn Archived khi chọn câu hỏi cho nội dung mới.
        // Các kiểm tra còn lại vẫn áp dụng cho cả hai trường hợp.
        else if (isNewUse
            && question.Status == QuestionStatus.Archived)
        {
            // Archived không được chọn cho nội dung mới.
            // Không xóa câu hỏi hoặc làm mất dữ liệu lịch sử.
            errors.Add(new QuestionError(
                nameof(Question.Status),
                "archived",
                "Không được sử dụng câu hỏi Archived cho nội dung mới."));
        }
        // 5. Bảo vệ trường hợp dữ liệu options bị null.
        // Phía đọc database vẫn phải Include Options trước khi gọi.
        if (question.Options == null)
        {
            errors.Add(new QuestionError(
                nameof(Question.Options),
                "required",
                "Không có dữ liệu lựa chọn để kiểm tra."));

            return errors.AsReadOnly();
        }
        // Chuyển options từ entity sang request của validator đã có.
        // Thao tác này chỉ tạo dữ liệu trong bộ nhớ, không lưu database.
        List<QuestionOptionRequest> optionRequests =
            new List<QuestionOptionRequest>();

        int optionIndex = 0;

        foreach (QuestionOption option in question.Options)
        {
            // Không để dữ liệu lỗi gây NullReferenceException.
            if (option == null)
            {
                errors.Add(new QuestionError(
                    $"Options[{optionIndex}]",
                    "required",
                    "Lựa chọn không được null."));

                return errors.AsReadOnly();
            }

            optionRequests.Add(new QuestionOptionRequest(
                option.Content,
                option.IsCorrect));

            optionIndex++;
        }
        CreateQuestionRequest request = new CreateQuestionRequest
        {
            Content = question.Content,
            PrimarySkill = question.PrimarySkill,
            Level = question.Level,
            Explanation = question.Explanation,
            StimulusId = question.StimulusId,
            Options = optionRequests
        };
        // Kiểm tra lại cấu trúc MCQ trên dữ liệu đã lưu:
        // - Nội dung hợp lệ.
        // - Có PrimarySkill hợp lệ.
        // - Có ít nhất 2 option.
        // - Có đúng 1 đáp án đúng.
        // - Nội dung option và metadata không vượt giới hạn.
        IReadOnlyList<QuestionError> structureErrors =
            _createQuestionValidator.Validate(request);

        foreach (QuestionError error in structureErrors)
        {
            errors.Add(error);
        }
        // 6. Nếu bài yêu cầu một skill cụ thể, câu hỏi phải khớp.
        ValidateTargetSkill(question, targetSkill, errors);

        // 7. Kiểm tra liên kết, ownership và audio của Stimulus.
        ValidateStimulus(question, ownerTeacherUserId, errors);

        return errors.AsReadOnly();

    }
    // Kiểm tra điều kiện riêng của Skill Assessment.
    // Không có targetSkill thì không áp dụng điều kiện này.
    private static void ValidateTargetSkill(
        Question question,
        EnglishSkill? targetSkill,
        List<QuestionError> errors)
    {
        if (!targetSkill.HasValue)
        {
            return;
        }

        if (!Enum.IsDefined(typeof(EnglishSkill), targetSkill.Value))
        {
            errors.Add(new QuestionError(
                nameof(targetSkill),
                "invalid",
                "TargetSkill của bài không hợp lệ."));

            return;
        }

        if (question.PrimarySkill != targetSkill.Value)
        {
            errors.Add(new QuestionError(
                nameof(Question.PrimarySkill),
                "skill_mismatch",
                "PrimarySkill của câu hỏi không khớp TargetSkill của bài."));
        }
    }

    // Kiểm tra Stimulus đã tải cùng câu hỏi.
    // Hàm chỉ kiểm tra dữ liệu, không tải tài nguyên từ Internet.
    private static void ValidateStimulus(
        Question question,
        Guid ownerTeacherUserId,
        List<QuestionError> errors)
    {
        bool isListening =
            question.PrimarySkill == EnglishSkill.Listening;

        // Không gắn Stimulus: được phép với câu hỏi không phải Listening.
        if (!question.StimulusId.HasValue)
        {
            // Entity đã đọc từ database phải có liên kết nhất quán.
            if (question.Stimulus != null)
            {
                errors.Add(new QuestionError(
                    nameof(Question.StimulusId),
                    "invalid",
                    "Liên kết Stimulus của câu hỏi không hợp lệ."));
            }

            if (isListening)
            {
                errors.Add(new QuestionError(
                    nameof(Question.StimulusId),
                    "audio_required",
                    "Câu hỏi Listening phải có audio stimulus."));
            }

            return;
        }

        Stimulus? stimulus = question.Stimulus;

        // Có ID nhưng không tải được Stimulus hoặc tải nhầm tài nguyên.
        // Phía gọi phải Include Stimulus trước khi gọi validator.
        if (stimulus == null
            || stimulus.Id != question.StimulusId.Value)
        {
            errors.Add(new QuestionError(
                nameof(Question.StimulusId),
                "invalid",
                "Stimulus không tồn tại hoặc chưa được tải đúng."));

            return;
        }

        // Teacher phải sở hữu cả Question và Stimulus.
        if (stimulus.OwnerTeacherUserId != ownerTeacherUserId)
        {
            errors.Add(new QuestionError(
                nameof(Question.StimulusId),
                "forbidden",
                "Stimulus không thuộc Teacher sở hữu bài."));

            return;
        }

        if (!Enum.IsDefined(
            typeof(StimulusType),
            stimulus.StimulusType))
        {
            errors.Add(new QuestionError(
                nameof(Stimulus.StimulusType),
                "invalid",
                "Loại Stimulus không hợp lệ."));

            return;
        }

        // Có passage không đủ để sử dụng câu hỏi Listening.
        if (isListening
            && stimulus.StimulusType != StimulusType.Audio)
        {
            errors.Add(new QuestionError(
                nameof(Question.StimulusId),
                "audio_required",
                "Câu hỏi Listening phải gắn Stimulus loại Audio."));

            return;
        }

        if (stimulus.StimulusType == StimulusType.Audio)
        {
            // Dùng đúng rule URL tài nguyên đang có trong dự án:
            // URL tuyệt đối HTTP/HTTPS, không vượt quá 2048 ký tự.
            bool validAudioUrl = StimulusAudioUrl.TryGetHash(stimulus.ResourceUrl, out _) || CourseRules.ValidResource(
                LessonResourceType.Audio,
                null,
                stimulus.ResourceUrl);

            if (!validAudioUrl)
            {
                errors.Add(new QuestionError(
                    nameof(Stimulus.ResourceUrl),
                    "invalid_audio_url",
                    "URL audio không hợp lệ."));
            }
        }

    }
}
