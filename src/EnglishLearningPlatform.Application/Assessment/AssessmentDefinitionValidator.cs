using System.ComponentModel.DataAnnotations;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Domain.Common;

using AssessmentEntity =
    EnglishLearningPlatform.Domain.Entities.Assessment;

namespace EnglishLearningPlatform.Application.Assessment;

public sealed class AssessmentDefinitionValidator
    : IAssessmentDefinitionValidator
{
    private readonly IQuestionUsageValidator _questionValidator;

    public AssessmentDefinitionValidator(
        IQuestionUsageValidator questionValidator)
    {
        if (questionValidator == null)
        {
            throw new ArgumentNullException(
                nameof(questionValidator));
        }

        _questionValidator = questionValidator;
    }

    /// <summary>
    /// Kiểm tra định nghĩa đề, không thay đổi entity.
    /// Quyền tài khoản, trạng thái, version và Attempt
    /// phải được service kiểm tra riêng trong transaction.
    /// </summary>
    public IReadOnlyList<string> Validate(
        AssessmentEntity assessment,
        IReadOnlyList<AssessmentQuestion> questions,
        Course? linkedCourse,
        IReadOnlyList<Course> finalCourses,
        bool isFirstPublish)
    {
        if (assessment == null)
        {
            throw new ArgumentNullException(nameof(assessment));
        }

        if (questions == null)
        {
            throw new ArgumentNullException(nameof(questions));
        }

        if (finalCourses == null)
        {
            throw new ArgumentNullException(nameof(finalCourses));
        }

        var errors = new List<string>();

        if (assessment.OwnerTeacherUserId == Guid.Empty)
        {
            errors.Add("Không xác định được Teacher sở hữu đề.");
            return errors.AsReadOnly();
        }

        ValidateConfiguration(assessment, errors);
        ValidateCourseAndFinal(
            assessment, linkedCourse, finalCourses, errors);

        EnglishSkill? targetSkill = null;

        if (assessment.AssessmentType
            == AssessmentType.SkillAssessment)
        {
            targetSkill = assessment.TargetSkill;
        }

        var questionIds = new HashSet<Guid>();
        var orderIndexes = new HashSet<int>();

        for (int index = 0; index < questions.Count; index++)
        {
            var item = questions[index];
            string prefix = $"Câu {index + 1}: ";

            if (item == null)
            {
                errors.Add(prefix + "Không có dữ liệu câu hỏi.");
                continue;
            }

            if (item.AssessmentId != assessment.Id)
            {
                errors.Add(
                    prefix + "Liên kết không thuộc Assessment này.");
            }

            if (item.QuestionId == Guid.Empty)
            {
                errors.Add(prefix + "ID câu hỏi không hợp lệ.");
                continue;
            }

            if (!questionIds.Add(item.QuestionId))
            {
                errors.Add(prefix + "Câu hỏi bị chọn trùng.");
            }

            if (item.OrderIndex < 0
                || !orderIndexes.Add(item.OrderIndex))
            {
                errors.Add(prefix + "Thứ tự câu hỏi không hợp lệ.");
            }

            // Khớp với decimal(8, 2) của AssessmentQuestion.Points.
            // Từ chối dữ liệu sai thay vì để SQL tự làm tròn.
            if (item.Points <= 0
                || item.Points > 999999.99m
                || decimal.Round(item.Points, 2) != item.Points)
            {
                errors.Add(
                    prefix
                    + "Points phải từ 0.01 đến 999999.99 "
                    + "và tối đa hai chữ số thập phân.");
            }

            var question = item.Question;

            // Không đọc tiếp nội dung ngữ liệu của owner khác.
            if (question == null
                || question.Id != item.QuestionId
                || question.OwnerTeacherUserId
                    != assessment.OwnerTeacherUserId)
            {
                errors.Add(
                    prefix
                    + "Câu hỏi không tồn tại hoặc không cùng owner.");

                continue;
            }

            IReadOnlyList<QuestionError> questionErrors;

            if (isFirstPublish)
            {
                // Publish lần đầu không đưa câu Archived
                // vào một đề mới được công bố.
                questionErrors =
                    _questionValidator.ValidateForNewUse(
                        assessment.OwnerTeacherUserId,
                        question,
                        targetSkill);
            }
            else
            {
                // Câu đã thuộc đề Published được kiểm tra
                // theo ExistingUse để không phá nội dung lịch sử.
                questionErrors =
                    _questionValidator.ValidateForExistingUse(
                        assessment.OwnerTeacherUserId,
                        question,
                        targetSkill);
            }

            foreach (var error in questionErrors)
            {
                errors.Add(prefix + error.Message);
            }

            // Validator dùng chung đã kiểm tra audio.
            // Kiểm tra thêm passage rỗng.
            var stimulus = question.Stimulus;

            // Đề cũ vẫn giữ URL ngoài để không sửa lịch sử. Đề công bố mới phải dùng bytes đã lưu bất biến.
            if (isFirstPublish && stimulus?.StimulusType == StimulusType.Audio
                && !StimulusAudioResource.TryGetHash(stimulus.ResourceUrl, out _))
                errors.Add(prefix + "Audio URL ngoài không bảo đảm bất biến. Hãy upload audio và dùng Question/Stimulus mới trước khi Publish.");

            if (stimulus != null
                && stimulus.OwnerTeacherUserId
                    == assessment.OwnerTeacherUserId
                && stimulus.StimulusType == StimulusType.Passage
                && string.IsNullOrWhiteSpace(stimulus.ContentText))
            {
                errors.Add(prefix + "Passage không được để trống.");
            }
        }

        // Đếm QuestionId khác nhau, không đếm số dòng liên kết.
        if (questionIds.Count < 5)
        {
            errors.Add(
                "Assessment phải có ít nhất 5 câu hỏi khác nhau.");
        }

        return errors.AsReadOnly();
    }

    /// <summary>
    /// Dùng lại validation cấu hình đã có ở Task 1.
    /// Việc tạo request dưới đây chỉ diễn ra trong bộ nhớ.
    /// </summary>
    private static void ValidateConfiguration(
        AssessmentEntity assessment,
        List<string> errors)
    {
        var configuration = new AssessmentDraftRequest
        {
            Title = assessment.Title,
            Description = assessment.Description,
            AssessmentType = assessment.AssessmentType,
            TargetSkill = assessment.TargetSkill,
            DurationMinutes = assessment.DurationMinutes,
            MaxAttempts = assessment.MaxAttempts,
            PassingScore = assessment.PassingScore
        };

        var validationResults = new List<ValidationResult>();

        Validator.TryValidateObject(
            configuration,
            new ValidationContext(configuration),
            validationResults,
            validateAllProperties: true);

        foreach (var result in validationResults)
        {
            errors.Add(
                "Cấu hình: "
                + (result.ErrorMessage ?? "Dữ liệu không hợp lệ."));
        }
    }

    /// <summary>
    /// Kiểm tra quan hệ Course/Final của định nghĩa sắp được công bố.
    /// Không gắn, gỡ hoặc thay Final tại đây.
    /// Service chịu trách nhiệm kiểm tra trạng thái Publish.
    /// </summary>
    private static void ValidateCourseAndFinal(
        AssessmentEntity assessment,
        Course? linkedCourse,
        IReadOnlyList<Course> finalCourses,
        List<string> errors)
    {
        if (assessment.CourseId.HasValue)
        {
            if (linkedCourse == null
                || linkedCourse.Id != assessment.CourseId.Value
                || linkedCourse.OwnerTeacherUserId
                    != assessment.OwnerTeacherUserId)
            {
                errors.Add(
                    "Course không tồn tại hoặc không cùng owner.");
            }
            else if (linkedCourse.Status == CourseStatus.Archived)
            {
                errors.Add(
                    "Không được công bố hoặc sửa định nghĩa đề "
                    + "trong Course đã Archived.");
            }
        }
        else if (linkedCourse != null)
        {
            errors.Add("Thông tin liên kết Course không nhất quán.");
        }

        foreach (var course in finalCourses)
        {
            if (course.FinalAssessmentId != assessment.Id)
            {
                errors.Add("Thông tin tham chiếu Final không nhất quán.");
                continue;
            }

            if (assessment.CourseId != course.Id
                || assessment.OwnerTeacherUserId
                    != course.OwnerTeacherUserId)
            {
                errors.Add(
                    "Assessment đang làm Final phải thuộc "
                    + "đúng Course và cùng owner.");
            }

            bool validFinalType =
                assessment.AssessmentType
                    == AssessmentType.SkillAssessment
                || assessment.AssessmentType
                    == AssessmentType.PracticeExam;

            if (!validFinalType)
            {
                errors.Add(
                    "Final chỉ được dùng Skill Assessment "
                    + "hoặc Practice Exam.");
            }

            if (!assessment.PassingScore.HasValue)
            {
                errors.Add("Assessment làm Final phải có PassingScore.");
            }
        }
    }
}
