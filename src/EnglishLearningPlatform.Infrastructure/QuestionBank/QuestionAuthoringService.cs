using System.Data;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishLearningPlatform.Infrastructure.QuestionBank;

public sealed class QuestionAuthoringService(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ICreateQuestionValidator validator,
    ILogger<QuestionAuthoringService> logger) : IQuestionAuthoringService
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

    public async Task<QuestionEditor?> GetEditorAsync(
        Guid actorUserId,
        Guid questionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!await IsActiveTeacherAsync(actorUserId))
        {
            return null;
        }

        var question = await db.Questions
            .AsNoTracking()
            .Include(q => q.Options)
            .SingleOrDefaultAsync(
                q => q.Id == questionId && q.OwnerTeacherUserId == actorUserId,
                cancellationToken);

        if (question is null)
        {
            return null;
        }

        var lockReason = await GetLockReasonAsync(questionId, cancellationToken);
        var options = question.Options
            .OrderBy(o => o.OrderIndex)
            .Select(o => new QuestionOptionItem
            {
                Id = o.Id,
                Content = o.Content,
                OrderIndex = o.OrderIndex,
                IsCorrect = o.IsCorrect
            })
            .ToArray();

        return new QuestionEditor(
            question.Id,
            question.Content,
            question.PrimarySkill,
            question.Level,
            question.Difficulty,
            question.Explanation,
            question.StimulusId,
            question.Status,
            question.CreatedAtUtc,
            options,
            lockReason is not null,
            lockReason);
    }

    public async Task<IReadOnlyList<QuestionStimulusOption>> GetStimuliAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!await IsActiveTeacherAsync(actorUserId))
        {
            return Array.Empty<QuestionStimulusOption>();
        }

        return await db.Stimuli
            .AsNoTracking()
            .Where(s => s.OwnerTeacherUserId == actorUserId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ThenByDescending(s => s.Id)
            .Select(s => new QuestionStimulusOption(
                s.Id,
                s.StimulusType,
                s.Title))
            .ToListAsync(cancellationToken);
    }

    public async Task<QuestionWriteResult> UpdateAsync(
        Guid actorUserId,
        Guid questionId,
        CreateQuestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await IsActiveTeacherAsync(actorUserId))
        {
            return QuestionWriteResult.Failure(
            [
                new QuestionError(string.Empty, "forbidden", "Bạn không có quyền sửa câu hỏi.")
            ]);
        }

        var validationErrors = validator.Validate(request);
        if (validationErrors.Count > 0)
        {
            return QuestionWriteResult.Failure(validationErrors);
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var question = await db.Questions
                .Include(q => q.Options)
                .SingleOrDefaultAsync(
                    q => q.Id == questionId && q.OwnerTeacherUserId == actorUserId,
                    cancellationToken);

            if (question is null)
            {
                return QuestionWriteResult.Failure(
                [
                    new QuestionError(string.Empty, "not_found", "Câu hỏi không tồn tại.")
                ]);
            }

            if (request.StimulusId is Guid stimulusId)
            {
                var stimulusOwned = await db.Stimuli
                    .AsNoTracking()
                    .AnyAsync(
                        s => s.Id == stimulusId && s.OwnerTeacherUserId == actorUserId,
                        cancellationToken);

                if (!stimulusOwned)
                {
                    return QuestionWriteResult.Failure(
                    [
                        new QuestionError(
                            nameof(CreateQuestionRequest.StimulusId),
                            "invalid",
                            "Stimulus không tồn tại hoặc không thuộc quyền sở hữu của bạn.")
                    ]);
                }
            }

            var scoringContentChanged = !ScoringContentEquals(question, request);
            var lockReason = await GetLockReasonAsync(questionId, cancellationToken);

            if (lockReason is not null && scoringContentChanged)
            {
                return QuestionWriteResult.Failure(
                [
                    new QuestionError(
                        nameof(CreateQuestionRequest.Content),
                        "locked",
                        lockReason),
                    new QuestionError(
                        nameof(CreateQuestionRequest.Options),
                        "locked",
                        "Không thể sửa lựa chọn hoặc đáp án đúng sau khi câu hỏi đã được sử dụng."),
                    new QuestionError(
                        nameof(CreateQuestionRequest.PrimarySkill),
                        "locked",
                        "Không thể đổi kỹ năng chính của câu hỏi đã bị khóa."),
                    new QuestionError(
                        nameof(CreateQuestionRequest.StimulusId),
                        "locked",
                        "Không thể đổi Stimulus của câu hỏi đã bị khóa.")
                ]);
            }

            if (scoringContentChanged)
            {
                question.Content = request.Content;
                question.PrimarySkill = request.PrimarySkill!.Value;
                question.StimulusId = request.StimulusId;

                db.QuestionOptions.RemoveRange(question.Options);

                for (var i = 0; i < request.Options.Count; i++)
                {
                    db.QuestionOptions.Add(new QuestionOption
                    {
                        QuestionId = question.Id,
                        Content = request.Options[i].Content,
                        IsCorrect = request.Options[i].IsCorrect,
                        OrderIndex = i
                    });
                }
            }

            question.Level = request.Level;
            question.Difficulty = request.Difficulty;
            question.Explanation = request.Explanation;
            question.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return QuestionWriteResult.Success(question.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Question update failed for {QuestionId}.", questionId);
            return QuestionWriteResult.Failure(
            [
                new QuestionError(
                    string.Empty,
                    "integration_error",
                    "Chưa thể lưu câu hỏi lúc này. Vui lòng thử lại sau.")
            ]);
        }
    }

    private async Task<string?> GetLockReasonAsync(Guid questionId, CancellationToken cancellationToken)
    {
        // Chỉ đọc hai thông tin cần để xác định câu hỏi có bị khóa không.
        // new { ... } tạo đối tượng chứa kết quả đọc, không tạo bản ghi database.
        var questionState = await db.Questions
            .AsNoTracking()
            .Where(q => q.Id == questionId)
            .Select(q => new
            {
                q.Status,
                q.ContentLockedAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        // Không tìm thấy Question thì không có thông tin khóa.
        // Quyền sở hữu và sự tồn tại đã được phương thức gọi kiểm tra riêng.
        if (questionState == null)
        {
            return null;
        }

        // Dấu khóa đã lưu được ưu tiên kiểm tra.
        // Không mở khóa chỉ vì trạng thái hoặc liên kết hiện tại thay đổi.
        if (questionState.ContentLockedAtUtc.HasValue)
        {
            return "Nội dung chấm của câu hỏi đã bị khóa để bảo toàn "
                + "nội dung đã công bố và dữ liệu lịch sử.";
        }

        // Giữ tên biến cũ để các kiểm tra phía dưới tiếp tục hoạt động.
        QuestionStatus questionStatus = questionState.Status;

        if (questionStatus == QuestionStatus.Published)
        {
            return "Question đã được công bố nên nội dung chấm đã bị khóa.";
        }

        if (questionStatus == QuestionStatus.Archived)
        {
            return "Question đã bị lưu trữ nên nội dung chấm đã bị khóa.";
        }

        // Assessment status != Draft covers Published, Unpublished, and Archived
        var publishedInAssessment = await db.AssessmentQuestions
            .AsNoTracking()
            .AnyAsync(
                item => item.QuestionId == questionId &&
                        item.Assessment.Status != AssessmentStatus.Draft,
                cancellationToken);

        if (publishedInAssessment)
        {
            return "Câu hỏi đã từng được công bố trong Assessment nên nội dung chấm đã bị khóa.";
        }

        // Practice status != Draft covers Published and Unpublished
        var publishedInPractice = await db.PracticeQuestions
            .AsNoTracking()
            .AnyAsync(
                item => item.QuestionId == questionId &&
                        item.Practice.Status != PracticeStatus.Draft,
                cancellationToken);

        if (publishedInPractice)
        {
            return "Câu hỏi đã từng được công bố trong Practice nên nội dung chấm đã bị khóa.";
        }

        var hasAssessmentAttempt = await db.Attempts
            .AsNoTracking()
            .AnyAsync(
                attempt => db.AssessmentQuestions.Any(question =>
                    question.AssessmentId == attempt.AssessmentId &&
                    question.QuestionId == questionId),
                cancellationToken);

        if (hasAssessmentAttempt)
        {
            return "Câu hỏi đã có dữ liệu làm Assessment nên nội dung chấm đã bị khóa.";
        }

        var hasPracticeSubmission = await db.PracticeSubmissions
            .AsNoTracking()
            .AnyAsync(
                submission => db.PracticeQuestions.Any(question =>
                    question.PracticeId == submission.PracticeId &&
                    question.QuestionId == questionId),
                cancellationToken);

        return hasPracticeSubmission
            ? "Câu hỏi đã có dữ liệu làm Practice nên nội dung chấm đã bị khóa."
            : null;
    }

    private static bool ScoringContentEquals(Question question, CreateQuestionRequest request)
    {
        if (question.Content != request.Content ||
            question.PrimarySkill != request.PrimarySkill ||
            question.StimulusId != request.StimulusId)
        {
            return false;
        }

        var current = question.Options.OrderBy(option => option.OrderIndex).ToArray();
        if (current.Length != request.Options.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Length; index++)
        {
            if (current[index].Content != request.Options[index].Content ||
                current[index].IsCorrect != request.Options[index].IsCorrect)
            {
                return false;
            }
        }

        return true;
    }
}
