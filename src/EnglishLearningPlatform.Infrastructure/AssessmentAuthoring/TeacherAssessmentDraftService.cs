using System.ComponentModel.DataAnnotations;
using System.Data;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Learning;
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

    public TeacherAssessmentDraftService(
        AppDbContext db,
        ILogger<TeacherAssessmentDraftService> logger)
    {
        if (db == null)
        {
            throw new ArgumentNullException(nameof(db));
        }

        if (logger == null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        _db = db;
        _logger = logger;
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

                CanEditDraft =
                    assessment.Status == AssessmentStatus.Draft
                    && !assessment.Attempts.Any()
            })
            .ToListAsync(cancellationToken);

        return new(
            LearningAccessResult.Allowed,
            items);
    }

    /// <summary>
    /// Đọc Draft thuộc Teacher để điền vào form.
    /// Không trả entity đang được EF theo dõi.
    /// </summary>
    public async Task<LearningOutcome<AssessmentDraftRequest>>
        GetDraftAsync(
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

        if (assessment.Status != AssessmentStatus.Draft || hasAttempt)
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

        return new(LearningAccessResult.Allowed, input);
    }

    /// <summary>
    /// Tạo hoặc sửa cấu hình Draft.
    /// Mọi thay đổi được commit cùng nhau hoặc rollback cùng nhau.
    /// </summary>
    public async Task<WriteOutcome> SaveDraftAsync(
        Guid actorUserId,
        Guid? assessmentId,
        AssessmentDraftRequest request,
        CancellationToken cancellationToken = default)
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

            if (courseId.HasValue)
            {
                var course = await _db.Courses
                    .FromSqlInterpolated(
                        $"SELECT * FROM Courses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {courseId.Value}")
                    .AsNoTracking()
                    .SingleOrDefaultAsync(cancellationToken);

                if (course == null
                    || course.OwnerTeacherUserId != actorUserId)
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

                if (existing.Status != AssessmentStatus.Draft
                    || hasAttempt)
                {
                    return Failure(
                        assessmentId.Value,
                        "Chỉ được sửa Assessment Draft chưa có Attempt.");
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

            // Chỉ chép những trường thuộc phạm vi cấu hình Draft.
            // Không chép owner, status, CourseId, Questions hoặc Attempts.
            assessment.Title = request.Title.Trim();
            assessment.Description = request.Description?.Trim();
            assessment.AssessmentType = request.AssessmentType!.Value;
            assessment.TargetSkill = request.TargetSkill;
            assessment.DurationMinutes = request.DurationMinutes;
            assessment.MaxAttempts = request.MaxAttempts;
            assessment.PassingScore = request.PassingScore;

            if (assessmentId.HasValue)
            {
                assessment.UpdatedAtUtc = DateTimeOffset.UtcNow;
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
                "Saving Assessment Draft failed for {AssessmentId}.",
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
        string message)
    {
        return new WriteOutcome(
            LearningAccessResult.Allowed,
            assessmentId,
            new Dictionary<string, string[]>
            {
                [string.Empty] = new string[] { message }
            });
    }
}