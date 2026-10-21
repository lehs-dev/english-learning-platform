using System.ComponentModel.DataAnnotations;
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

using AssessmentEntity =
    EnglishLearningPlatform.Domain.Entities.Assessment;

namespace EnglishLearningPlatform.Infrastructure.AssessmentAuthoring;

public sealed class TeacherAssessmentDraftService
    : ITeacherAssessmentDraftService
{
    private readonly AppDbContext _db;
    private readonly ILogger<TeacherAssessmentDraftService> _logger;
    private readonly IAssessmentDefinitionValidator _definitionValidator;
    private readonly IStimulusAudioStore _audioStore;

    public TeacherAssessmentDraftService(
        AppDbContext db,
        ILogger<TeacherAssessmentDraftService> logger,
        IAssessmentDefinitionValidator definitionValidator, IStimulusAudioStore audioStore)
    {
        if (db == null)
        {
            throw new ArgumentNullException(nameof(db));
        }

        if (logger == null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        if (definitionValidator == null)
        {
            throw new ArgumentNullException(nameof(definitionValidator));
        }

        _db = db;
        _logger = logger;
        _definitionValidator = definitionValidator;
        _audioStore = audioStore;
    }

    /// <summary>
    /// Đọc danh sách riêng của Teacher.
    /// Không lấy nội dung câu hỏi hoặc kết quả của Student.
    /// </summary>
    public async Task<
        LearningOutcome<IReadOnlyList<TeacherAssessmentListItem>>>
        GetListAsync(
            Guid actorUserId,
            CancellationToken cancellationToken = default)
    {
        if (!await IsActiveTeacherAsync(actorUserId, cancellationToken))
        {
            return new(
                LearningAccessResult.Forbidden);
        }

        var items = await _db.Assessments
            .AsNoTracking()
            .Where(assessment =>
                assessment.OwnerTeacherUserId == actorUserId)
            .OrderByDescending(assessment => assessment.CreatedAtUtc)
            .ThenByDescending(assessment => assessment.Id)
            .Select(assessment => new TeacherAssessmentListItem
            {
                Id = assessment.Id,
                Title = assessment.Title,
                AssessmentType = assessment.AssessmentType,
                Status = assessment.Status,
                DurationMinutes = assessment.DurationMinutes,
                MaxAttempts = assessment.MaxAttempts,
                PassingScore = assessment.PassingScore,
                QuestionCount = assessment.Questions.Count,
                CourseId = assessment.CourseId,
                UpdatedAtUtc = assessment.UpdatedAtUtc,
                // Không tiết lộ tên Course khác owner nếu liên kết cũ bị sai.
                CourseTitle = assessment.Course != null
                    && assessment.Course.OwnerTeacherUserId == actorUserId
                        ? assessment.Course.Title : null,

                CanEditDraft =
                    assessment.Status == AssessmentStatus.Draft
                    && !assessment.Attempts.Any(),

                CanEditConfiguration =
                    (assessment.Status == AssessmentStatus.Draft
                        || assessment.Status == AssessmentStatus.Published)
                    && !assessment.Attempts.Any()
            })
            .ToListAsync(cancellationToken);

        return new(
            LearningAccessResult.Allowed,
            items);
    }

    /// <summary>
    /// Đọc cấu hình Draft/Published thuộc Teacher khi chưa có Attempt.
    /// Không trả entity đang được EF theo dõi.
    /// </summary>
    public async Task<LearningOutcome<TeacherAssessmentConfigurationEditor>>
        GetConfigurationEditorAsync(
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
            .SingleOrDefaultAsync(
                item =>
                    item.Id == assessmentId
                    && item.OwnerTeacherUserId == actorUserId,
                cancellationToken);

        if (assessment == null)
        {
            // Không tiết lộ bài không tồn tại hay thuộc Teacher khác.
            return new(LearningAccessResult.NotFound);
        }

        if (assessment.CourseId.HasValue)
        {
            bool ownsCourse = await _db.Courses
                .AsNoTracking()
                .AnyAsync(
                    course =>
                        course.Id == assessment.CourseId.Value
                        && course.OwnerTeacherUserId == actorUserId,
                    cancellationToken);

            if (!ownsCourse)
            {
                return new(LearningAccessResult.NotFound);
            }
        }

        bool hasAttempt = await _db.Attempts
            .AnyAsync(
                attempt => attempt.AssessmentId == assessmentId,
                cancellationToken);

        bool editableStatus =
            assessment.Status == AssessmentStatus.Draft
            || assessment.Status == AssessmentStatus.Published;

        if (!editableStatus || hasAttempt)
        {
            return new(LearningAccessResult.Forbidden);
        }

        var input = new AssessmentDraftRequest
        {
            Title = assessment.Title,
            Description = assessment.Description,
            AssessmentType = assessment.AssessmentType,
            TargetSkill = assessment.TargetSkill,
            DurationMinutes = assessment.DurationMinutes,
            MaxAttempts = assessment.MaxAttempts,
            PassingScore = assessment.PassingScore,
            ExpectedUpdatedAtUtc = assessment.UpdatedAtUtc
        };

        return new(
            LearningAccessResult.Allowed,
            new TeacherAssessmentConfigurationEditor
            {
                Status = assessment.Status,
                Input = input
            });
    }

    /// <summary>
    /// Giữ hợp đồng Draft-only cho các chức năng đang dùng hàm cũ.
    /// Không tự mở quyền soạn câu hỏi Published.
    /// </summary>
    public async Task<LearningOutcome<AssessmentDraftRequest>> GetDraftAsync(
        Guid actorUserId,
        Guid assessmentId,
        CancellationToken cancellationToken = default)
    {
        var result = await GetConfigurationEditorAsync(
            actorUserId, assessmentId, cancellationToken);

        if (result.Access != LearningAccessResult.Allowed)
        {
            return new(result.Access);
        }

        if (result.Value == null)
        {
            return new(LearningAccessResult.NotFound);
        }

        if (result.Value.Status != AssessmentStatus.Draft)
        {
            return new(LearningAccessResult.Forbidden);
        }

        return new(LearningAccessResult.Allowed, result.Value.Input);
    }

    // Caller cũ chỉ được lưu Draft.
    public Task<WriteOutcome> SaveDraftAsync(
        Guid actorUserId,
        Guid? assessmentId,
        AssessmentDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        return SaveConfigurationCoreAsync(
            actorUserId, assessmentId, request,
            allowPublished: false, cancellationToken: cancellationToken);
    }

    // Trang cấu hình được sửa Published, nhưng chỉ trước Attempt đầu tiên.
    public Task<WriteOutcome> SaveConfigurationAsync(
        Guid actorUserId,
        Guid? assessmentId,
        AssessmentDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        return SaveConfigurationCoreAsync(
            actorUserId, assessmentId, request,
            allowPublished: true, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Lưu cấu hình theo phạm vi cho phép của caller.
    /// Mọi thay đổi được commit cùng nhau hoặc rollback cùng nhau.
    /// </summary>
    private async Task<WriteOutcome> SaveConfigurationCoreAsync(
        Guid actorUserId,
        Guid? assessmentId,
        AssessmentDraftRequest request,
        bool allowPublished,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!await IsActiveTeacherAsync(
                actorUserId, cancellationToken))
            {
                return WriteOutcome.Denied(
                    LearningAccessResult.Forbidden);
            }

            // Đọc trước CourseId để giữ thứ tự khóa:
            // Course trước, Assessment sau, giống luồng Student.
            Guid? courseId = null;

            if (assessmentId.HasValue)
            {
                var snapshot = await _db.Assessments
                    .AsNoTracking()
                    .Where(item =>
                        item.Id == assessmentId.Value
                        && item.OwnerTeacherUserId == actorUserId)
                    .Select(item => new { item.CourseId })
                    .SingleOrDefaultAsync(cancellationToken);

                if (snapshot == null)
                {
                    return WriteOutcome.Denied(
                        LearningAccessResult.NotFound);
                }

                courseId = snapshot.CourseId;
            }

            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            Course? linkedCourse = null;

            if (courseId.HasValue)
            {
                linkedCourse = await _db.Courses
                    .FromSqlInterpolated(
                        $"SELECT * FROM Courses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {courseId.Value}")
                    .AsNoTracking()
                    .SingleOrDefaultAsync(cancellationToken);

                if (linkedCourse == null
                    || linkedCourse.OwnerTeacherUserId != actorUserId)
                {
                    return WriteOutcome.Denied(
                        LearningAccessResult.NotFound);
                }
            }

            AssessmentEntity assessment;

            if (assessmentId.HasValue)
            {
                var existing = await _db.Assessments
                    .FromSqlInterpolated(
                        $"SELECT * FROM Assessments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {assessmentId.Value} AND OwnerTeacherUserId = {actorUserId}")
                    .SingleOrDefaultAsync(cancellationToken);

                if (existing == null)
                {
                    return WriteOutcome.Denied(
                        LearningAccessResult.NotFound);
                }

                // Không tin dữ liệu entity đã được đọc trước đó.
                await _db.Entry(existing)
                    .ReloadAsync(cancellationToken);

                if (existing.CourseId != courseId)
                {
                    return Failure(
                        assessmentId.Value,
                        "Bài kiểm tra đã thay đổi liên kết Course. "
                        + "Hãy tải lại trang.");
                }

                bool hasAttempt = await _db.Attempts
                    .AnyAsync(
                        attempt =>
                            attempt.AssessmentId == assessmentId.Value,
                        cancellationToken);

                bool editableStatus =
                    existing.Status == AssessmentStatus.Draft
                    || (allowPublished
                        && existing.Status == AssessmentStatus.Published);

                if (!editableStatus || hasAttempt)
                {
                    return Failure(
                        assessmentId.Value,
                        allowPublished
                            ? "Chỉ được sửa cấu hình Draft hoặc Published chưa có Attempt."
                            : "Chỉ được sửa Assessment Draft chưa có Attempt.");
                }

                if (existing.UpdatedAtUtc
                    != request.ExpectedUpdatedAtUtc)
                {
                    return Failure(
                        assessmentId.Value,
                        "Bài kiểm tra đã được cập nhật ở nơi khác. "
                        + "Hãy tải lại trang trước khi lưu.");
                }

                assessment = existing;
            }
            else
            {
                // Server quyết định owner, trạng thái và scope ban đầu.
                assessment = new AssessmentEntity
                {
                    OwnerTeacherUserId = actorUserId,
                    Status = AssessmentStatus.Draft,
                    CourseId = null
                };
            }

            // Kiểm tra lại tài khoản ngay trước bước ghi.
            if (!await IsActiveTeacherAsync(
                actorUserId, cancellationToken))
            {
                return WriteOutcome.Denied(
                    LearningAccessResult.Forbidden);
            }

            var errors = Validate(request);

            if (errors.Count > 0)
            {
                return new WriteOutcome(
                    LearningAccessResult.Allowed,
                    assessmentId ?? Guid.Empty,
                    errors);
            }

            // Kiểm tra bản dự kiến trước khi thay đổi entity thật.
            // Không lấy owner, trạng thái hay CourseId từ form.
            var candidate = new AssessmentEntity
            {
                Id = assessment.Id,
                OwnerTeacherUserId = assessment.OwnerTeacherUserId,
                CourseId = assessment.CourseId,
                Status = assessment.Status,
                Title = request.Title.Trim(),
                Description = request.Description?.Trim(),
                AssessmentType = request.AssessmentType!.Value,
                TargetSkill = request.TargetSkill,
                DurationMinutes = request.DurationMinutes,
                MaxAttempts = request.MaxAttempts,
                PassingScore = request.PassingScore
            };

            var publishedQuestions = new List<AssessmentQuestion>();

            if (assessment.Status == AssessmentStatus.Published)
            {
                // Dùng câu hỏi đã lưu; theo dõi entity để giữ dấu khóa sau validation.
                publishedQuestions = await _db.AssessmentQuestions
                    .Where(item => item.AssessmentId == assessment.Id)
                    .Include(item => item.Question)
                        .ThenInclude(question => question.Options)
                    .Include(item => item.Question)
                        .ThenInclude(question => question.Stimulus)
                    .AsSingleQuery()
                    .OrderBy(item => item.OrderIndex)
                    .ThenBy(item => item.Id)
                    .ToListAsync(cancellationToken);

                var finalCourses = await _db.Courses
                    .AsNoTracking()
                    .Where(course => course.FinalAssessmentId == assessment.Id)
                    .ToListAsync(cancellationToken);

                var definitionErrors = _definitionValidator.Validate(
                    candidate, publishedQuestions, linkedCourse,
                    finalCourses, isFirstPublish: false);

                if (definitionErrors.Count > 0)
                {
                    return Failure(assessment.Id, definitionErrors.ToArray());
                }
                string? audioError = await StimulusAudioIntegrity.GetErrorAsync(
                    publishedQuestions.Select(q => q.Question.Stimulus), _audioStore, cancellationToken);
                if (audioError != null) return Failure(assessment.Id, audioError);
            }

            // Chỉ chép những trường thuộc phạm vi cấu hình.
            // Không chép owner, status, CourseId, Questions hoặc Attempts.
            assessment.Title = request.Title.Trim();
            assessment.Description = request.Description?.Trim();
            assessment.AssessmentType = request.AssessmentType!.Value;
            assessment.TargetSkill = request.TargetSkill;
            assessment.DurationMinutes = request.DurationMinutes;
            assessment.MaxAttempts = request.MaxAttempts;
            assessment.PassingScore = request.PassingScore;

            DateTimeOffset now = DateTimeOffset.UtcNow;

            // Nội dung đã công bố vẫn bị khóa, kể cả sửa cấu hình trước Attempt.
            // LockContent giữ nguyên thời điểm khóa nếu đã có.
            foreach (var item in publishedQuestions)
            {
                item.Question.LockContent(now);
                item.Question.Stimulus?.LockContent(now);
            }

            if (assessmentId.HasValue)
            {
                assessment.UpdatedAtUtc = now;
            }
            else
            {
                _db.Assessments.Add(assessment);
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return WriteOutcome.Ok(assessment.Id);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Không ghi nội dung đề hoặc dữ liệu nhạy cảm vào log.
            _logger.LogError(
                exception,
                "Saving Assessment configuration failed for {AssessmentId}.",
                assessmentId);

            return Failure(
                assessmentId ?? Guid.Empty,
                "Chưa thể lưu bài kiểm tra. Vui lòng thử lại.");
        }
    }

    /// <summary>
    /// Kiểm tra tài khoản hiện tại trong database.
    /// Giữ cùng quy tắc Active và đúng một role Teacher của dự án.
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
    /// Chạy lại validation tại service.
    /// Không phụ thuộc caller có đi qua MVC hay không.
    /// </summary>
    private static IReadOnlyDictionary<string, string[]> Validate(
        AssessmentDraftRequest request)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        var errors = new Dictionary<string, string[]>();

        foreach (var result in results)
        {
            string field = result.MemberNames.FirstOrDefault()
                ?? string.Empty;

            if (!errors.ContainsKey(field))
            {
                errors[field] = Array.Empty<string>();
            }

            var messages = new List<string>(errors[field]);

            messages.Add(
                result.ErrorMessage ?? "Dữ liệu không hợp lệ.");

            errors[field] = messages.ToArray();
        }

        return errors;
    }

    /// <summary>
    /// Trả lỗi chung để giao diện hiển thị ở đầu form.
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
