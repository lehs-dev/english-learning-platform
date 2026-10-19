using System.Data;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishLearningPlatform.Infrastructure.AssessmentAuthoring;

public sealed class TeacherAssessmentQuestionService
    : ITeacherAssessmentQuestionService
{
    private readonly AppDbContext _db;
    private readonly ITeacherAssessmentDraftService _draftService;
    private readonly IQuestionUsageValidator _questionValidator;
    private readonly ILogger<TeacherAssessmentQuestionService> _logger;

    public TeacherAssessmentQuestionService(
        AppDbContext db,
        ITeacherAssessmentDraftService draftService,
        IQuestionUsageValidator questionValidator,
        ILogger<TeacherAssessmentQuestionService> logger)
    {
        if (db == null)
        {
            throw new ArgumentNullException(nameof(db));
        }

        if (draftService == null)
        {
            throw new ArgumentNullException(nameof(draftService));
        }

        if (questionValidator == null)
        {
            throw new ArgumentNullException(nameof(questionValidator));
        }

        if (logger == null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        _db = db;
        _draftService = draftService;
        _questionValidator = questionValidator;
        _logger = logger;
    }

    /// <summary>
    /// Đọc danh sách đã lưu, độc lập với trang tìm kiếm Question Bank.
    /// </summary>
    public async Task<LearningOutcome<TeacherAssessmentQuestionEditor>>
        GetEditorAsync(
            Guid actorUserId,
            Guid assessmentId,
            CancellationToken cancellationToken = default)
    {
        var access = await _draftService.GetDraftAsync(
            actorUserId, assessmentId, cancellationToken);

        if (access.Access != LearningAccessResult.Allowed)
        {
            return new LearningOutcome<TeacherAssessmentQuestionEditor>(
                access.Access);
        }

        var editor = await _db.Assessments
            .AsNoTracking()
            .Where(assessment =>
                assessment.Id == assessmentId
                && assessment.OwnerTeacherUserId == actorUserId
                && assessment.Status == AssessmentStatus.Draft
                && !assessment.Attempts.Any()
                // Không trả nội dung Question của Teacher khác
                // nếu database đang có liên kết bất thường.
                && !assessment.Questions.Any(item =>
                    item.Question.OwnerTeacherUserId != actorUserId))
            .Select(assessment => new TeacherAssessmentQuestionEditor
            {
                AssessmentId = assessment.Id,
                Title = assessment.Title,
                AssessmentType = assessment.AssessmentType,
                TargetSkill = assessment.TargetSkill,
                UpdatedAtUtc = assessment.UpdatedAtUtc,

                Items = assessment.Questions
                    .OrderBy(item => item.OrderIndex)
                    .ThenBy(item => item.Id)
                    .Select(item => new SelectedAssessmentQuestion
                    {
                        QuestionId = item.QuestionId,
                        Content = item.Question.Content,
                        PrimarySkill = item.Question.PrimarySkill,
                        Status = item.Question.Status,
                        Points = item.Points
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (editor == null)
        {
            return new LearningOutcome<TeacherAssessmentQuestionEditor>(
                LearningAccessResult.NotFound);
        }

        return new LearningOutcome<TeacherAssessmentQuestionEditor>(
            LearningAccessResult.Allowed,
            editor);
    }

    /// <summary>
    /// Lưu câu hỏi, thứ tự và points trong cùng transaction.
    /// Không sửa nội dung Question, Stimulus hoặc kết quả Student.
    /// </summary>
    public async Task<WriteOutcome> SaveAsync(
        Guid actorUserId,
        Guid assessmentId,
        UpdateAssessmentQuestionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var access = await _draftService.GetDraftAsync(
                actorUserId, assessmentId, cancellationToken);

            if (access.Access != LearningAccessResult.Allowed)
            {
                return WriteOutcome.Denied(access.Access);
            }

            // Đọc CourseId để giữ cùng thứ tự khóa với luồng Student:
            // Course -> Assessment.
            var snapshot = await _db.Assessments
                .AsNoTracking()
                .Where(assessment =>
                    assessment.Id == assessmentId
                    && assessment.OwnerTeacherUserId == actorUserId)
                .Select(assessment => new { assessment.CourseId })
                .SingleOrDefaultAsync(cancellationToken);

            if (snapshot == null)
            {
                return WriteOutcome.Denied(
                    LearningAccessResult.NotFound);
            }

            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            if (snapshot.CourseId.HasValue)
            {
                var course = await _db.Courses
                    .FromSqlInterpolated(
                        $"SELECT * FROM Courses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {snapshot.CourseId.Value}")
                    .AsNoTracking()
                    .SingleOrDefaultAsync(cancellationToken);

                if (course == null
                    || course.OwnerTeacherUserId != actorUserId)
                {
                    return WriteOutcome.Denied(
                        LearningAccessResult.NotFound);
                }
            }

            var assessment = await _db.Assessments
                .FromSqlInterpolated(
                    $"SELECT * FROM Assessments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {assessmentId} AND OwnerTeacherUserId = {actorUserId}")
                .SingleOrDefaultAsync(cancellationToken);

            if (assessment == null)
            {
                return WriteOutcome.Denied(
                    LearningAccessResult.NotFound);
            }

            await _db.Entry(assessment)
                .ReloadAsync(cancellationToken);

            if (assessment.CourseId != snapshot.CourseId)
            {
                return Failure(
                    assessmentId,
                    "Liên kết Course đã thay đổi. Hãy tải lại trang.");
            }

            // Kiểm tra lại trong transaction.
            // Không chỉ tin quyền/trạng thái đã đọc trước khi giữ khóa.
            access = await _draftService.GetDraftAsync(
                actorUserId, assessmentId, cancellationToken);

            if (access.Access != LearningAccessResult.Allowed)
            {
                return WriteOutcome.Denied(access.Access);
            }

            if (assessment.UpdatedAtUtc
                != request.ExpectedUpdatedAtUtc)
            {
                return Failure(
                    assessmentId,
                    "Bài kiểm tra đã thay đổi ở nơi khác. "
                    + "Hãy tải lại trang trước khi lưu.");
            }

            if (request.Items == null)
            {
                return Failure(
                    assessmentId,
                    "Request thiếu danh sách câu hỏi.");
            }

            if (assessment.AssessmentType
                    == AssessmentType.SkillAssessment
                && (!assessment.TargetSkill.HasValue
                    || !Enum.IsDefined(
                        typeof(EnglishSkill),
                        assessment.TargetSkill.Value)))
            {
                return Failure(
                    assessmentId,
                    "Skill Assessment chưa có TargetSkill hợp lệ.");
            }

            var oldItems = await _db.AssessmentQuestions
                .Where(item => item.AssessmentId == assessmentId)
                .ToListAsync(cancellationToken);

            var existingQuestionIds = new HashSet<Guid>(
                oldItems.Select(item => item.QuestionId));

            var requestedIds = request.Items
                .Where(item => item != null)
                .Select(item => item.QuestionId)
                .ToArray();

            // Chỉ đọc Question thuộc Teacher hiện tại.
            // Validator cần Options và Stimulus đã được tải đầy đủ.
            var questions = await _db.Questions
                .AsNoTracking()
                .Where(question =>
                    question.OwnerTeacherUserId == actorUserId
                    && requestedIds.Contains(question.Id))
                .Include(question => question.Options)
                .Include(question => question.Stimulus)
                .ToDictionaryAsync(
                    question => question.Id,
                    cancellationToken);

            EnglishSkill? targetSkill = null;

            if (assessment.AssessmentType
                == AssessmentType.SkillAssessment)
            {
                targetSkill = assessment.TargetSkill;
            }

            var seenIds = new HashSet<Guid>();
            var newItems = new List<AssessmentQuestion>();

            for (int index = 0; index < request.Items.Count; index++)
            {
                var input = request.Items[index];

                if (input == null || input.QuestionId == Guid.Empty)
                {
                    return Failure(
                        assessmentId,
                        $"Câu {index + 1}: ID câu hỏi không hợp lệ.");
                }

                if (!seenIds.Add(input.QuestionId))
                {
                    return Failure(
                        assessmentId,
                        "Không được chọn trùng câu hỏi.");
                }

                if (!input.Points.HasValue
                    || input.Points.Value <= 0
                    || input.Points.Value > 999999.99m
                    || decimal.Round(input.Points.Value, 2)
                        != input.Points.Value)
                {
                    return Failure(
                        assessmentId,
                        $"Câu {index + 1}: points phải từ 0.01 "
                        + "đến 999999.99 và tối đa hai chữ số thập phân.");
                }

                questions.TryGetValue(
                    input.QuestionId,
                    out Question? question);

                IReadOnlyList<QuestionError> errors;

                if (existingQuestionIds.Contains(input.QuestionId))
                {
                    // Chỉ dùng ExistingUse khi liên kết đã có thật trong DB.
                    errors = _questionValidator.ValidateForExistingUse(
                        actorUserId, question, targetSkill);
                }
                else
                {
                    // Câu mới thêm phải bị chặn nếu Archived.
                    errors = _questionValidator.ValidateForNewUse(
                        actorUserId, question, targetSkill);
                }

                if (errors.Count > 0)
                {
                    return Failure(
                        assessmentId,
                        $"Câu {index + 1}: {errors[0].Message}");
                }

                // Validator hiện tại đã kiểm tra audio.
                // Bổ sung kiểm tra passage tại luồng này,
                // không thay đổi validator dùng chung của nhóm.
                if (question?.Stimulus != null
                    && question.Stimulus.StimulusType
                        == StimulusType.Passage
                    && string.IsNullOrWhiteSpace(
                        question.Stimulus.ContentText))
                {
                    return Failure(
                        assessmentId,
                        $"Câu {index + 1}: passage không được để trống.");
                }

                // Mới tạo object trong bộ nhớ, chưa ghi database.
                newItems.Add(new AssessmentQuestion
                {
                    AssessmentId = assessmentId,
                    QuestionId = input.QuestionId,
                    Points = input.Points.Value,
                    OrderIndex = index
                });
            }

            // Chỉ tới đây khi TOÀN BỘ danh sách mới đã hợp lệ.
            // Draft không có Attempt nên được thay các bản ghi liên kết.
            if (oldItems.Count > 0)
            {
                _db.AssessmentQuestions.RemoveRange(oldItems);

                // Xóa trước để không đụng unique key QuestionId/OrderIndex.
                // Nếu thao tác phía sau lỗi, transaction rollback cả bước này.
                await _db.SaveChangesAsync(cancellationToken);
            }

            _db.AssessmentQuestions.AddRange(newItems);
            assessment.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return WriteOutcome.Ok(assessmentId);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Saving Assessment questions failed for {AssessmentId}.",
                assessmentId);

            return Failure(
                assessmentId,
                "Chưa thể lưu câu hỏi. Vui lòng thử lại.");
        }
    }

    /// <summary>
    /// Trả lỗi chung cho danh sách đang soạn.
    /// </summary>
    private static WriteOutcome Failure(
        Guid assessmentId,
        string message)
    {
        return new WriteOutcome(
            LearningAccessResult.Allowed,
            assessmentId,
            new Dictionary<string, string[]>
            {
                ["Items"] = new string[] { message }
            });
    }
}