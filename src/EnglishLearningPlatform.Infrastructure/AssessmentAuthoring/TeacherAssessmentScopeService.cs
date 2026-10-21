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

public sealed class TeacherAssessmentScopeService : ITeacherAssessmentScopeService
{
    private readonly AppDbContext _db;
    private readonly IAssessmentDefinitionValidator _definitionValidator;
    private readonly ILogger<TeacherAssessmentScopeService> _logger;
    private readonly IStimulusAudioStore _audioStore;

    public TeacherAssessmentScopeService(
        AppDbContext db,
        IAssessmentDefinitionValidator definitionValidator,
        ILogger<TeacherAssessmentScopeService> logger, IStimulusAudioStore audioStore)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _definitionValidator = definitionValidator
            ?? throw new ArgumentNullException(nameof(definitionValidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _audioStore = audioStore;
    }

    /// <summary>
    /// Cho owner xem phạm vi kể cả khi đề đã khóa.
    /// Quyền xem metadata không đồng nghĩa với quyền thay đổi Course.
    /// </summary>
    public async Task<LearningOutcome<TeacherAssessmentScopeEditor>> GetEditorAsync(
        Guid actorUserId,
        Guid assessmentId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsActiveTeacherAsync(actorUserId, cancellationToken))
        {
            return new(LearningAccessResult.Forbidden);
        }

        var assessment = await _db.Assessments
            .AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.Id == assessmentId
                && item.OwnerTeacherUserId == actorUserId,
                cancellationToken);

        if (assessment == null)
        {
            return new(LearningAccessResult.NotFound);
        }

        Course? currentCourse = null;

        if (assessment.CourseId.HasValue)
        {
            currentCourse = await _db.Courses
                .AsNoTracking()
                .SingleOrDefaultAsync(course =>
                    course.Id == assessment.CourseId.Value
                    && course.OwnerTeacherUserId == actorUserId,
                    cancellationToken);

            // Không trả tên Course của Teacher khác nếu dữ liệu cũ sai owner.
            if (currentCourse == null)
            {
                return new(LearningAccessResult.NotFound);
            }
        }

        bool hasAttempt = await _db.Attempts
            .AnyAsync(attempt => attempt.AssessmentId == assessmentId,
                cancellationToken);

        bool isFinal = await _db.Courses
            .AnyAsync(course => course.FinalAssessmentId == assessmentId,
                cancellationToken);

        string? lockReason = null;

        if (!IsEditableStatus(assessment.Status))
        {
            lockReason = "Trạng thái hiện tại không cho phép đổi phạm vi Assessment.";
        }
        else if (hasAttempt)
        {
            lockReason = "Assessment đã có Attempt, không được đổi Course hoặc phạm vi truy cập.";
        }
        else if (isFinal)
        {
            lockReason = "Hãy gỡ/thay Final hợp lệ trước khi đổi phạm vi Assessment.";
        }

        var courses = await _db.Courses
            .AsNoTracking()
            .Where(course => course.OwnerTeacherUserId == actorUserId
                && (course.Status == CourseStatus.Draft
                    || course.Status == CourseStatus.Published
                    || course.Status == CourseStatus.Unpublished))
            .OrderBy(course => course.Title)
            .ThenBy(course => course.Id)
            .Select(course => new AssessmentScopeCourseOption(
                course.Id, course.Title, course.Status))
            .ToListAsync(cancellationToken);

        return new(LearningAccessResult.Allowed,
            new TeacherAssessmentScopeEditor
            {
                AssessmentId = assessment.Id,
                Title = assessment.Title,
                Status = assessment.Status,
                CourseId = assessment.CourseId,
                CourseTitle = currentCourse?.Title,
                UpdatedAtUtc = assessment.UpdatedAtUtc,
                HasAttempt = hasAttempt,
                IsFinal = isFinal,
                CanChangeScope = lockReason == null,
                LockReason = lockReason,
                Courses = courses
            });
    }

    /// <summary>
    /// Gắn, gỡ hoặc chuyển Course trong một transaction.
    /// Không tự sửa Final và không thay đổi nội dung/Attempt của đề.
    /// </summary>
    public async Task<WriteOutcome> SaveAsync(
        Guid actorUserId,
        Guid assessmentId,
        AssessmentScopeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!await IsActiveTeacherAsync(actorUserId, cancellationToken))
            {
                return WriteOutcome.Denied(LearningAccessResult.Forbidden);
            }

            var snapshot = await _db.Assessments
                .AsNoTracking()
                .Where(item => item.Id == assessmentId
                    && item.OwnerTeacherUserId == actorUserId)
                .Select(item => new { item.CourseId })
                .SingleOrDefaultAsync(cancellationToken);

            if (snapshot == null)
            {
                return WriteOutcome.Denied(LearningAccessResult.NotFound);
            }

            if (request.CourseId == Guid.Empty)
            {
                return Failure(assessmentId, "Course được chọn không hợp lệ.");
            }

            // Đọc ID trước transaction để biết toàn bộ Course cần khóa.
            // Không tin những giá trị này sau khi đã lấy khóa Assessment.
            var finalCourseIds = await _db.Courses
                .AsNoTracking()
                .Where(course => course.FinalAssessmentId == assessmentId)
                .Select(course => course.Id)
                .ToListAsync(cancellationToken);

            var courseIdsToLock = new HashSet<Guid>(finalCourseIds);

            if (snapshot.CourseId.HasValue)
            {
                courseIdsToLock.Add(snapshot.CourseId.Value);
            }

            if (request.CourseId.HasValue)
            {
                courseIdsToLock.Add(request.CourseId.Value);
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            var lockedCourses = new Dictionary<Guid, Course>();

            // Mọi yêu cầu khóa Course theo cùng thứ tự ID rồi mới Assessment.
            // Hai yêu cầu chuyển A -> B và B -> A không lấy khóa ngược nhau.
            foreach (Guid courseId in courseIdsToLock.OrderBy(id => id))
            {
                var course = await _db.Courses
                    .FromSqlInterpolated(
                        $"SELECT * FROM Courses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {courseId}")
                    .AsNoTracking()
                    .SingleOrDefaultAsync(cancellationToken);

                if (course != null)
                {
                    lockedCourses.Add(course.Id, course);
                }
            }

            var assessment = await _db.Assessments
                .FromSqlInterpolated(
                    $"SELECT * FROM Assessments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {assessmentId} AND OwnerTeacherUserId = {actorUserId}")
                .SingleOrDefaultAsync(cancellationToken);

            if (assessment == null)
            {
                return WriteOutcome.Denied(LearningAccessResult.NotFound);
            }

            // DbContext có thể đã theo dõi entity cũ; đọc lại dữ liệu thật.
            await _db.Entry(assessment).ReloadAsync(cancellationToken);

            if (assessment.OwnerTeacherUserId != actorUserId)
            {
                return WriteOutcome.Denied(LearningAccessResult.NotFound);
            }

            if (assessment.CourseId != snapshot.CourseId)
            {
                return Failure(assessmentId,
                    "Liên kết Course đã thay đổi. Hãy tải lại trang trước khi lưu.");
            }

            if (!await IsActiveTeacherAsync(actorUserId, cancellationToken))
            {
                return WriteOutcome.Denied(LearningAccessResult.Forbidden);
            }

            if (!IsEditableStatus(assessment.Status))
            {
                return Failure(assessmentId,
                    "Chỉ được đổi phạm vi Assessment Draft, Published hoặc Unpublished.");
            }

            // Start cũng giữ khóa Assessment trước khi thêm Attempt.
            // Start thắng trước thì ở đây thấy Attempt và từ chối thay đổi.
            bool hasAttempt = await _db.Attempts
                .AnyAsync(attempt => attempt.AssessmentId == assessmentId,
                    cancellationToken);

            if (hasAttempt)
            {
                return Failure(assessmentId,
                    "Assessment đã có Attempt, không được đổi Course hoặc phạm vi truy cập.");
            }

            if (assessment.UpdatedAtUtc != request.ExpectedUpdatedAtUtc)
            {
                return Failure(assessmentId,
                    "Assessment đã thay đổi ở nơi khác. Hãy tải lại trang trước khi lưu.");
            }

            if (assessment.CourseId.HasValue
                && (!lockedCourses.TryGetValue(assessment.CourseId.Value,
                        out Course? currentCourse)
                    || currentCourse.OwnerTeacherUserId != actorUserId))
            {
                return WriteOutcome.Denied(LearningAccessResult.NotFound);
            }

            // Gửi lại cùng lựa chọn không làm đổi timestamp hoặc dữ liệu.
            // Tuy vậy vẫn phải qua quyền, trạng thái, Attempt và version ở trên.
            if (assessment.CourseId == request.CourseId)
            {
                await transaction.CommitAsync(cancellationToken);
                return WriteOutcome.Ok(assessmentId);
            }

            var finalCourses = await _db.Courses
                .AsNoTracking()
                .Where(course => course.FinalAssessmentId == assessmentId)
                .ToListAsync(cancellationToken);

            if (finalCourses.Count > 0)
            {
                return Failure(assessmentId,
                    "Hãy gỡ/thay Final hợp lệ trước khi đổi phạm vi Assessment. "
                    + "Thao tác này không tự gỡ Final của Course.");
            }

            Course? targetCourse = null;

            if (request.CourseId.HasValue)
            {
                if (!lockedCourses.TryGetValue(request.CourseId.Value, out targetCourse)
                    || targetCourse.OwnerTeacherUserId != actorUserId)
                {
                    // Không tiết lộ Course tồn tại hay thuộc Teacher khác.
                    return Failure(assessmentId,
                        "Course không tồn tại hoặc không thuộc Teacher hiện tại.");
                }

                if (targetCourse.Status != CourseStatus.Draft
                    && targetCourse.Status != CourseStatus.Published
                    && targetCourse.Status != CourseStatus.Unpublished)
                {
                    return Failure(assessmentId,
                        "Không được gắn Assessment vào Course đã Archived hoặc không hợp lệ.");
                }
            }

            if (assessment.Status == AssessmentStatus.Published)
            {
                // Đề Published phải vẫn hợp lệ sau khi đổi scope.
                // Tạo bản dự kiến, chưa sửa entity thật khi validation có thể lỗi.
                var candidate = new Assessment
                {
                    Id = assessment.Id,
                    OwnerTeacherUserId = assessment.OwnerTeacherUserId,
                    CourseId = request.CourseId,
                    Title = assessment.Title,
                    Description = assessment.Description,
                    AssessmentType = assessment.AssessmentType,
                    TargetSkill = assessment.TargetSkill,
                    DurationMinutes = assessment.DurationMinutes,
                    MaxAttempts = assessment.MaxAttempts,
                    PassingScore = assessment.PassingScore,
                    Status = assessment.Status
                };

                var questions = await _db.AssessmentQuestions
                    .AsNoTracking()
                    .Where(item => item.AssessmentId == assessmentId)
                    .Include(item => item.Question)
                        .ThenInclude(question => question.Options)
                    .Include(item => item.Question)
                        .ThenInclude(question => question.Stimulus)
                    .AsSingleQuery()
                    .OrderBy(item => item.OrderIndex)
                    .ThenBy(item => item.Id)
                    .ToListAsync(cancellationToken);

                var errors = _definitionValidator.Validate(
                    candidate, questions, targetCourse, finalCourses,
                    isFirstPublish: false);

                if (errors.Count > 0)
                {
                    return Failure(assessmentId, errors.ToArray());
                }
                string? audioError = await StimulusAudioIntegrity.GetErrorAsync(
                    questions.Select(q => q.Question.Stimulus), _audioStore, cancellationToken);
                if (audioError != null) return Failure(assessmentId, audioError);
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;

            // Bảo đảm version mới khác version cũ ngay cả khi clock quay lùi.
            if (assessment.UpdatedAtUtc.HasValue
                && now <= assessment.UpdatedAtUtc.Value)
            {
                now = assessment.UpdatedAtUtc.Value.AddTicks(1);
            }

            // Chỉ thay phạm vi. Không sửa cấu hình, câu hỏi, Final hoặc lịch sử.
            assessment.CourseId = request.CourseId;
            assessment.UpdatedAtUtc = now;

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
            // Transaction chưa commit sẽ rollback, không để đổi scope một phần.
            _logger.LogError(exception,
                "Changing Assessment scope failed for {AssessmentId}.", assessmentId);

            return Failure(assessmentId,
                "Chưa thể đổi phạm vi Assessment. Hãy tải lại trang và thử lại.");
        }
    }

    private static bool IsEditableStatus(AssessmentStatus status)
    {
        return status == AssessmentStatus.Draft
            || status == AssessmentStatus.Published
            || status == AssessmentStatus.Unpublished;
    }

    // Cookie không đủ: tài khoản thật phải Active và có duy nhất role Teacher.
    private Task<bool> IsActiveTeacherAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        return _db.Users.AsNoTracking().AnyAsync(user =>
            user.Id == actorUserId
            && user.AccountStatus == AccountStatus.Active
            && _db.UserRoles.Count(userRole => userRole.UserId == actorUserId) == 1
            && _db.UserRoles.Any(userRole =>
                userRole.UserId == actorUserId
                && _db.Roles.Any(role =>
                    role.Id == userRole.RoleId && role.Name == AppRoles.Teacher)),
            cancellationToken);
    }

    private static WriteOutcome Failure(Guid assessmentId, params string[] messages)
    {
        return new WriteOutcome(LearningAccessResult.Allowed, assessmentId,
            new Dictionary<string, string[]> { [string.Empty] = messages });
    }
}
