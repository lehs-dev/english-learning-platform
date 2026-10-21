using System.Data;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Infrastructure.Stimuli;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishLearningPlatform.Infrastructure.AssessmentAuthoring;

public sealed class TeacherAssessmentPublishService
    : ITeacherAssessmentPublishService
{
    private readonly AppDbContext _db;
    private readonly IAssessmentDefinitionValidator _validator;
    private readonly ILogger<TeacherAssessmentPublishService> _logger;
    private readonly IStimulusAudioStore _audioStore;

    public TeacherAssessmentPublishService(
        AppDbContext db,
        IAssessmentDefinitionValidator validator,
        ILogger<TeacherAssessmentPublishService> logger,
        IStimulusAudioStore audioStore)
    {
        if (db == null)
        {
            throw new ArgumentNullException(nameof(db));
        }

        if (validator == null)
        {
            throw new ArgumentNullException(nameof(validator));
        }

        if (logger == null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        _db = db;
        _validator = validator;
        _logger = logger;
        _audioStore = audioStore;
    }

    /// <summary>
    /// Kiểm tra toàn bộ Draft trước khi công bố.
    /// Trạng thái và dấu khóa nội dung được lưu cùng một transaction.
    /// </summary>
    public async Task<WriteOutcome> PublishAsync(
        Guid actorUserId,
        Guid assessmentId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!await IsActiveTeacherAsync(
                actorUserId, cancellationToken))
            {
                return WriteOutcome.Denied(
                    LearningAccessResult.Forbidden);
            }

            // Đọc CourseId trước để xác định thứ tự khóa.
            // Không lấy CourseId từ dữ liệu người dùng gửi lên.
            var snapshot = await _db.Assessments
                .AsNoTracking()
                .Where(assessment =>
                    assessment.Id == assessmentId
                    && assessment.OwnerTeacherUserId == actorUserId)
                .Select(assessment => new
                {
                    assessment.CourseId
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (snapshot == null)
            {
                // Không tiết lộ đề thuộc Teacher khác.
                return WriteOutcome.Denied(
                    LearningAccessResult.NotFound);
            }

            // Serializable giữ dữ liệu được đọc ổn định
            // trong quá trình kiểm tra và công bố.
            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            Course? linkedCourse = null;

            if (snapshot.CourseId.HasValue)
            {
                // Giữ thứ tự Course -> Assessment,
                // giống luồng xử lý Assessment đang có.
                linkedCourse = await _db.Courses
                    .FromSqlInterpolated(
                        $"SELECT * FROM Courses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {snapshot.CourseId.Value}")
                    .AsNoTracking()
                    .SingleOrDefaultAsync(cancellationToken);

                if (linkedCourse == null
                    || linkedCourse.OwnerTeacherUserId != actorUserId)
                {
                    return WriteOutcome.Denied(
                        LearningAccessResult.NotFound);
                }
            }

            // Khóa Assessment để hai yêu cầu không cùng
            // sửa hoặc Publish đề này trong một thời điểm.
            var assessment = await _db.Assessments
                .FromSqlInterpolated(
                    $"SELECT * FROM Assessments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {assessmentId} AND OwnerTeacherUserId = {actorUserId}")
                .SingleOrDefaultAsync(cancellationToken);

            if (assessment == null)
            {
                return WriteOutcome.Denied(
                    LearningAccessResult.NotFound);
            }

            // Lấy lại giá trị thật, không dùng bản entity
            // có thể đã được DbContext theo dõi từ trước.
            await _db.Entry(assessment)
                .ReloadAsync(cancellationToken);

            if (assessment.OwnerTeacherUserId != actorUserId)
            {
                return WriteOutcome.Denied(
                    LearningAccessResult.NotFound);
            }

            if (assessment.CourseId != snapshot.CourseId)
            {
                return Failure(
                    assessmentId,
                    "Liên kết Course đã thay đổi. Hãy tải lại trang.");
            }

            // Kiểm tra lại tài khoản trong transaction.
            if (!await IsActiveTeacherAsync(
                actorUserId, cancellationToken))
            {
                return WriteOutcome.Denied(
                    LearningAccessResult.Forbidden);
            }

            // Bước này chỉ thực hiện Publish lần đầu từ Draft.
            // Không tự đổi Unpublished hoặc Archived thành Published.
            if (assessment.Status != AssessmentStatus.Draft)
            {
                return Failure(
                    assessmentId,
                    "Chỉ được Publish Assessment đang ở trạng thái Draft.");
            }

            bool hasAttempt = await _db.Attempts
                .AnyAsync(
                    attempt => attempt.AssessmentId == assessmentId,
                    cancellationToken);

            if (hasAttempt)
            {
                return Failure(
                    assessmentId,
                    "Assessment đã có Attempt, không được xử lý như Draft.");
            }

            if (assessment.UpdatedAtUtc != expectedUpdatedAtUtc)
            {
                return Failure(
                    assessmentId,
                    "Đề đã được cập nhật ở nơi khác. "
                    + "Hãy tải lại và kiểm tra trước khi Publish.");
            }

            // Đọc danh sách đã lưu, gồm dữ liệu cần để kiểm tra MCQ
            // và passage/audio. Không nhận danh sách này từ form.
            var questions = await _db.AssessmentQuestions
                .Where(item => item.AssessmentId == assessmentId)
                .Include(item => item.Question)
                    .ThenInclude(question => question.Options)
                .Include(item => item.Question)
                    .ThenInclude(question => question.Stimulus)
                .AsSingleQuery()
                .OrderBy(item => item.OrderIndex)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

            // Kiểm tra cả những Course đang chọn đề này làm Final.
            // Không chỉ kiểm tra CourseId trên Assessment.
            var finalCourses = await _db.Courses
                .AsNoTracking()
                .Where(course =>
                    course.FinalAssessmentId == assessmentId)
                .ToListAsync(cancellationToken);

            var errors = _validator.Validate(
                assessment,
                questions,
                linkedCourse,
                finalCourses,
                isFirstPublish: true);

            if (errors.Count > 0)
            {
                // Chưa thay đổi trạng thái hay dấu khóa nào.
                return Failure(
                    assessmentId,
                    errors.ToArray());
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string? audioError = await StimulusAudioIntegrity.GetErrorAsync(
                questions.Select(q => q.Question.Stimulus), _audioStore, cancellationToken);
            if (audioError != null) return Failure(assessmentId, audioError);

            // Chỉ khóa nội dung sau khi toàn bộ đề hợp lệ.
            // LockContent giữ nguyên thời điểm khóa đã có.
            foreach (var item in questions)
            {
                item.Question.LockContent(now);

                if (item.Question.Stimulus != null)
                {
                    item.Question.Stimulus.LockContent(now);
                }
            }

            // Không đổi Question.Status hoặc quan hệ Course/Final.
            assessment.Status = AssessmentStatus.Published;
            assessment.UpdatedAtUtc = now;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return WriteOutcome.Ok(assessmentId);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Giữ đúng ý nghĩa request bị hủy.
            throw;
        }
        catch (Exception exception)
        {
            // Transaction chưa commit sẽ rollback khi được giải phóng.
            // Không ghi nội dung câu hỏi hoặc đáp án vào log.
            _logger.LogError(
                exception,
                "Publishing Assessment failed for {AssessmentId}.",
                assessmentId);

            return Failure(
                assessmentId,
                "Chưa thể Publish Assessment. Vui lòng thử lại.");
        }
    }

    /// <summary>
    /// Giữ đúng quy tắc tài khoản của dự án:
    /// Active và có duy nhất role Teacher.
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
                        userRole =>
                            userRole.UserId == actorUserId) == 1
                    && _db.UserRoles.Any(
                        userRole =>
                            userRole.UserId == actorUserId
                            && _db.Roles.Any(
                                role =>
                                    role.Id == userRole.RoleId
                                    && role.Name == AppRoles.Teacher)),
                cancellationToken);
    }

    /// <summary>
    /// Trả danh sách lỗi chung để giao diện hiển thị.
    /// params cho phép truyền một hoặc nhiều thông báo.
    /// </summary>
    private static WriteOutcome Failure(
        Guid assessmentId,
        params string[] messages)
    {
        return new WriteOutcome(
            LearningAccessResult.Allowed,
            assessmentId,
            new Dictionary<string, string[]>
            {
                [string.Empty] = messages
            });
    }
}
