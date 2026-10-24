using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.QuestionBank;
public sealed class QuestionCreationService(
    AppDbContext db,
    IActiveUserValidator activeUsers,
    ICreateQuestionValidator createQuestionValidator) : IQuestionCreationService
{
    public async Task<QuestionWriteResult> CreateAsync(
    Guid actorUserId,
    CreateQuestionRequest request,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Kiểm tra quyền.
        if (!await activeUsers.IsActiveTeacherAsync(actorUserId, cancellationToken))
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
                        "Ngữ liệu không tồn tại hoặc không thuộc quyền sở hữu của bạn.")
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
