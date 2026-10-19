using System.ComponentModel.DataAnnotations;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using AssessmentEntity =
    EnglishLearningPlatform.Domain.Entities.Assessment;

namespace EnglishLearningPlatform.Infrastructure.AssessmentAuthoring;

public sealed class TeacherAssessmentPreviewService
    : ITeacherAssessmentPreviewService
{
    private readonly AppDbContext _db;
    private readonly IQuestionUsageValidator _questionValidator;

    public TeacherAssessmentPreviewService(
        AppDbContext db,
        IQuestionUsageValidator questionValidator)
    {
        if (db == null)
        {
            throw new ArgumentNullException(nameof(db));
        }

        if (questionValidator == null)
        {
            throw new ArgumentNullException(
                nameof(questionValidator));
        }

        _db = db;
        _questionValidator = questionValidator;
    }

    /// <summary>
    /// Đọc đề đã lưu để Preview.
    /// Không tạo hoặc thay đổi bất kỳ dữ liệu làm bài nào.
    /// </summary>
    public async Task<LearningOutcome<TeacherAssessmentPreview>>
        GetAsync(
            Guid actorUserId,
            Guid assessmentId,
            CancellationToken cancellationToken = default)
    {
        if (!await IsActiveTeacherAsync(
            actorUserId, cancellationToken))
        {
            return new LearningOutcome<TeacherAssessmentPreview>(
                LearningAccessResult.Forbidden);
        }

        // AsNoTracking: dữ liệu chỉ dùng để đọc, không theo dõi sửa đổi.
        // Không Include Attempts, Answers hoặc SkillResults.
        var assessment = await _db.Assessments
            .AsNoTracking()
            .Where(item =>
                item.Id == assessmentId
                && item.OwnerTeacherUserId == actorUserId)
            .Include(item => item.Course)
            .Include(item => item.Questions)
                .ThenInclude(item => item.Question)
                    .ThenInclude(question => question.Options)
            .Include(item => item.Questions)
                .ThenInclude(item => item.Question)
                    .ThenInclude(question => question.Stimulus)
            .AsSingleQuery()
            .SingleOrDefaultAsync(cancellationToken);

        if (assessment == null)
        {
            // Không phân biệt bài không tồn tại với bài của Teacher khác.
            return new LearningOutcome<TeacherAssessmentPreview>(
                LearningAccessResult.NotFound);
        }

        if (assessment.Status != AssessmentStatus.Draft
            && assessment.Status != AssessmentStatus.Published)
        {
            return new LearningOutcome<TeacherAssessmentPreview>(
                LearningAccessResult.Forbidden);
        }

        // Assessment gắn Course phải có cùng owner với Course.
        if (assessment.CourseId.HasValue
            && (assessment.Course == null
                || assessment.Course.OwnerTeacherUserId
                    != actorUserId))
        {
            return new LearningOutcome<TeacherAssessmentPreview>(
                LearningAccessResult.NotFound);
        }

        var items = assessment.Questions
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .ToList();

        // Phòng trường hợp database có liên kết sai owner:
        // không trả nội dung Question/Stimulus của người khác.
        foreach (var item in items)
        {
            var question = item.Question;

            if (question == null
                || question.OwnerTeacherUserId != actorUserId)
            {
                return new LearningOutcome<TeacherAssessmentPreview>(
                    LearningAccessResult.NotFound);
            }

            if (question.Stimulus != null
                && question.Stimulus.OwnerTeacherUserId
                    != actorUserId)
            {
                return new LearningOutcome<TeacherAssessmentPreview>(
                    LearningAccessResult.NotFound);
            }
        }

        var questions =
            new List<AssessmentQuestionTakingModel>();

        foreach (var item in items)
        {
            var question = item.Question;
            var options = new List<AssessmentOptionTakingModel>();

            // Giữ đúng thứ tự lựa chọn đã lưu trong Question Bank.
            foreach (var option in question.Options
                .OrderBy(option => option.OrderIndex)
                .ThenBy(option => option.Id))
            {
                options.Add(new AssessmentOptionTakingModel(
                    option.Id,
                    option.Content,
                    option.OrderIndex));
            }

            string? audioUrl = null;
            var stimulus = question.Stimulus;

            // Chỉ đưa URL HTTP/HTTPS hợp lệ vào trình phát.
            // Hàm này không tải hoặc kiểm tra file từ Internet.
            if (stimulus != null
                && stimulus.StimulusType == StimulusType.Audio
                && CourseRules.ValidResource(
                    LessonResourceType.Audio,
                    null,
                    stimulus.ResourceUrl))
            {
                audioUrl = stimulus.ResourceUrl;
            }

            questions.Add(new AssessmentQuestionTakingModel(
                item.Id,
                question.Id,
                question.Content,
                item.Points,
                stimulus?.ContentText,
                null, // Preview không đọc đáp án của Attempt.
                options)
            {
                AudioUrl = audioUrl
            });
        }

        var preview = new TeacherAssessmentPreview
        {
            AssessmentId = assessment.Id,
            Title = assessment.Title,
            Description = assessment.Description,
            AssessmentType = assessment.AssessmentType,
            Status = assessment.Status,
            TargetSkill = assessment.TargetSkill,
            CourseId = assessment.CourseId,
            CourseTitle = assessment.Course?.Title,
            DurationMinutes = assessment.DurationMinutes,
            MaxAttempts = assessment.MaxAttempts,
            PassingScore = assessment.PassingScore,
            Questions = questions,
            Warnings = GetWarnings(assessment, items)
        };

        return new LearningOutcome<TeacherAssessmentPreview>(
            LearningAccessResult.Allowed,
            preview);
    }

    /// <summary>
    /// Giữ cùng quy tắc tài khoản với Task 1:
    /// Active và đúng một role Teacher.
    /// </summary>
    private Task<bool> IsActiveTeacherAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        return _db.Users
            .AsNoTracking()
            .AnyAsync(
                user =>
                    user.Id == actorUserId
                    && user.AccountStatus == AccountStatus.Active
                    && _db.UserRoles.Count(
                        item => item.UserId == actorUserId) == 1
                    && _db.UserRoles.Any(
                        item =>
                            item.UserId == actorUserId
                            && _db.Roles.Any(
                                role =>
                                    role.Id == item.RoleId
                                    && role.Name == AppRoles.Teacher)),
                cancellationToken);
    }

    /// <summary>
    /// Cảnh báo cấu hình/nội dung đang thiếu.
    /// Không thay đổi trạng thái và không thay thế validator Publish.
    /// </summary>
    private List<string> GetWarnings(
        AssessmentEntity assessment,
        IReadOnlyList<AssessmentQuestion> items)
    {
        var warnings = new List<string>();

        // Tái sử dụng các rule cấu hình của Task 1.
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

        var configurationErrors = new List<ValidationResult>();

        Validator.TryValidateObject(
            configuration,
            new ValidationContext(configuration),
            configurationErrors,
            validateAllProperties: true);

        foreach (var error in configurationErrors)
        {
            warnings.Add(
                "Cấu hình: "
                + (error.ErrorMessage ?? "Dữ liệu không hợp lệ."));
        }

        int distinctCount = items
            .Select(item => item.QuestionId)
            .Distinct()
            .Count();

        // Theo TBR08 trong Project.md:
        // Publish cần tối thiểu 5 câu khác nhau.
        // Thiếu câu chỉ cảnh báo, không chặn Preview.
        if (distinctCount < 5)
        {
            warnings.Add(
                $"Đề mới có {distinctCount}/5 câu hỏi khác nhau.");
        }

        if (distinctCount != items.Count)
        {
            warnings.Add("Đề đang có câu hỏi trùng.");
        }

        EnglishSkill? targetSkill = null;

        if (assessment.AssessmentType
            == AssessmentType.SkillAssessment)
        {
            targetSkill = assessment.TargetSkill;
        }

        for (int index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var question = item.Question;

            if (item.Points <= 0
                || item.Points > 999999.99m
                || decimal.Round(item.Points, 2) != item.Points)
            {
                warnings.Add(
                    $"Câu {index + 1}: points không hợp lệ.");
            }

            // Đây là câu đã thuộc đề, không phải thêm câu mới.
            // Không chặn Preview chỉ vì câu đã Archived.
            var errors =
                _questionValidator.ValidateForExistingUse(
                    assessment.OwnerTeacherUserId,
                    question,
                    targetSkill);

            foreach (var error in errors)
            {
                warnings.Add(
                    $"Câu {index + 1}: {error.Message}");
            }

            // Validator dùng chung đã kiểm tra audio.
            // Kiểm tra thêm passage rỗng giống Task 2.
            if (question.Stimulus != null
                && question.Stimulus.StimulusType
                    == StimulusType.Passage
                && string.IsNullOrWhiteSpace(
                    question.Stimulus.ContentText))
            {
                warnings.Add(
                    $"Câu {index + 1}: passage đang để trống.");
            }
        }

        return warnings;
    }
}