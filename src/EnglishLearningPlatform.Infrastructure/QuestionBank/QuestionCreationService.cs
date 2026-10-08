using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.QuestionBank;
public sealed class QuestionCreationService(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ICreateQuestionValidator createQuestionValidator) : IQuestionCreationService
{
    private async Task<bool> IsActiveTeacherAsync(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            return false;
        }

        var user = await userManager.FindByIdAsync(userId.ToString());

        if (user is null || user.AccountStatus != AccountStatus.Active)
        {
            return false;
        }

        var roles = await userManager.GetRolesAsync(user);

        return roles.Count == 1 && roles[0] == AppRoles.Teacher;
    }
    public async Task<QuestionWriteResult> CreateAsync(
    Guid actorUserId,
    CreateQuestionRequest request,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Kiểm tra quyền.
        if (!await IsActiveTeacherAsync(actorUserId))
        {
            return QuestionWriteResult.Failure(
            [
                new QuestionError(
                    string.Empty,
                    "forbidden",
                    "Bạn không có quyền tạo câu hỏi.")
            ]);
        }

        // 2. Kiểm tra dữ liệu đầu vào.
        var errors = createQuestionValidator.Validate(request);

        if (errors.Count > 0)
        {
            return QuestionWriteResult.Failure(errors);
        }

        // 3. Kiểm tra quyền sử dụng Stimulus.
        if (request.StimulusId is Guid stimulusId)
        {
            var canUseStimulus = await db.Stimuli
                .AsNoTracking()
                .AnyAsync(
                    stimulus =>
                        stimulus.Id == stimulusId
                        && stimulus.OwnerTeacherUserId == actorUserId,
                    cancellationToken);

            if (!canUseStimulus)
            {
                return QuestionWriteResult.Failure(
                [
                    new QuestionError(
                        nameof(request.StimulusId),
                        "invalid",
                        "Stimulus không tồn tại hoặc không thuộc quyền sở hữu của bạn.")
                ]);
            }
        }

        // 4. Tạo Question sau khi các kiểm tra đã đạt.
        var question = new Question
        {
            OwnerTeacherUserId = actorUserId,
            Content = request.Content,
            PrimarySkill = request.PrimarySkill!.Value,
            Level = request.Level,
            Difficulty = request.Difficulty,
            Explanation = request.Explanation,
            StimulusId = request.StimulusId,
            Status = QuestionStatus.Draft
        };

        // 5. Tạo Options và gán thứ tự ở server.
        for (var index = 0; index < request.Options.Count; index++)
        {
            var option = request.Options[index];

            question.Options.Add(new QuestionOption
            {
                Content = option.Content,
                IsCorrect = option.IsCorrect,
                OrderIndex = index
            });
        }

        // 6. Lưu Question và Options.
        db.Questions.Add(question);
        await db.SaveChangesAsync(cancellationToken);

        return QuestionWriteResult.Success(question.Id);
    }


}