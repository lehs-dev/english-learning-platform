using System.Data;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Infrastructure.Stimuli;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishLearningPlatform.Infrastructure.AssessmentAuthoring;

public sealed class TeacherCourseFinalService : ITeacherCourseFinalService
{
    private readonly AppDbContext _db;
    private readonly IAssessmentDefinitionValidator _validator;
    private readonly ILogger<TeacherCourseFinalService> _logger;
    private readonly IStimulusAudioStore _audioStore;

    public TeacherCourseFinalService(AppDbContext db, IAssessmentDefinitionValidator validator,
        ILogger<TeacherCourseFinalService> logger, IStimulusAudioStore audioStore)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _audioStore = audioStore;
    }

    /// <summary>Chỉ đọc metadata của owner; GET không đổi Final hoặc completion.</summary>
    public async Task<LearningOutcome<TeacherCourseFinalEditor>> GetEditorAsync(
        Guid actorUserId, Guid courseId, CancellationToken cancellationToken = default)
    {
        if (!await IsTeacherAsync(actorUserId, cancellationToken))
            return new(LearningAccessResult.Forbidden);

        var course = await _db.Courses.AsNoTracking().SingleOrDefaultAsync(
            c => c.Id == courseId && c.OwnerTeacherUserId == actorUserId, cancellationToken);
        if (course == null) return new(LearningAccessResult.NotFound);

        Assessment? current = null;
        if (course.FinalAssessmentId.HasValue)
        {
            current = await _db.Assessments.AsNoTracking().SingleOrDefaultAsync(a =>
                a.Id == course.FinalAssessmentId.Value && a.CourseId == courseId
                && a.OwnerTeacherUserId == actorUserId, cancellationToken);
            // Không tiết lộ đề khác owner nếu dữ liệu liên kết cũ không nhất quán.
            if (current == null) return new(LearningAccessResult.NotFound);
        }

        bool used = current != null && await _db.Attempts.AnyAsync(
            a => a.AssessmentId == current.Id, cancellationToken);
        string? reason = null;
        if (course.Status == CourseStatus.Archived)
            reason = "Course đã Archived, không được gắn/thay/gỡ Final.";
        else if (used)
            reason = "Final đã có Attempt. Không được thay/gỡ Final, kể cả bằng bản clone.";

        var options = await _db.Assessments.AsNoTracking().Where(a =>
            a.CourseId == courseId && a.OwnerTeacherUserId == actorUserId
            && a.Status == AssessmentStatus.Published
            && (a.AssessmentType == AssessmentType.SkillAssessment || a.AssessmentType == AssessmentType.PracticeExam)
            && a.PassingScore.HasValue && a.PassingScore >= 0 && a.PassingScore <= 100)
            .OrderBy(a => a.Title).ThenBy(a => a.Id)
            .Select(a => new FinalAssessmentOption(a.Id, a.Title, a.AssessmentType, a.PassingScore!.Value))
            .ToListAsync(cancellationToken);

        return new(LearningAccessResult.Allowed, new TeacherCourseFinalEditor
        {
            CourseId = course.Id, CourseTitle = course.Title, CourseStatus = course.Status,
            FinalAssessmentId = current?.Id, FinalAssessmentTitle = current?.Title,
            FinalAssessmentStatus = current?.Status, UpdatedAtUtc = course.UpdatedAtUtc,
            HasFinalAttempts = used, CanChangeFinal = reason == null, LockReason = reason,
            Assessments = options
        });
    }

    /// <summary>
    /// Khóa Course trước Assessment, giống Start và đổi scope.
    /// Mọi kiểm tra xong mới đổi FK; lỗi không để lại Final/completion dở dang.
    /// </summary>
    public async Task<WriteOutcome> SaveAsync(Guid actorUserId, Guid courseId,
        CourseFinalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!await IsTeacherAsync(actorUserId, cancellationToken))
                return WriteOutcome.Denied(LearningAccessResult.Forbidden);

            if (!await _db.Courses.AsNoTracking().AnyAsync(c =>
                c.Id == courseId && c.OwnerTeacherUserId == actorUserId, cancellationToken))
                return WriteOutcome.Denied(LearningAccessResult.NotFound);

            // Loại ID khác owner/Course trước khi lấy khóa Assessment của người khác.
            if (request.FinalAssessmentId.HasValue && !await _db.Assessments.AsNoTracking().AnyAsync(
                a => a.Id == request.FinalAssessmentId.Value && a.OwnerTeacherUserId == actorUserId
                    && a.CourseId == courseId, cancellationToken))
                return Failure(courseId, "Assessment không thuộc đúng Course và Teacher hiện tại.");

            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var course = await _db.Courses.FromSqlInterpolated(
                $"SELECT * FROM Courses WITH (UPDLOCK, HOLDLOCK) WHERE Id = {courseId} AND OwnerTeacherUserId = {actorUserId}")
                .SingleOrDefaultAsync(cancellationToken);
            if (course == null) return WriteOutcome.Denied(LearningAccessResult.NotFound);
            await _db.Entry(course).ReloadAsync(cancellationToken);
            if (!await IsTeacherAsync(actorUserId, cancellationToken))
                return WriteOutcome.Denied(LearningAccessResult.Forbidden);
            if (course.OwnerTeacherUserId != actorUserId)
                return WriteOutcome.Denied(LearningAccessResult.NotFound);
            if (course.Status == CourseStatus.Archived)
                return Failure(courseId, "Course đã Archived, không được gắn/thay/gỡ Final.");
            if (course.UpdatedAtUtc != request.ExpectedUpdatedAtUtc
                || course.FinalAssessmentId != request.ExpectedFinalAssessmentId)
                return Failure(courseId, "Final hoặc Course đã thay đổi. Hãy tải lại trang trước khi lưu.");

            var ids = new HashSet<Guid>();
            if (course.FinalAssessmentId.HasValue) ids.Add(course.FinalAssessmentId.Value);
            if (request.FinalAssessmentId.HasValue) ids.Add(request.FinalAssessmentId.Value);
            var locked = new Dictionary<Guid, Assessment>();
            foreach (Guid id in ids.OrderBy(id => id))
            {
                var assessment = await _db.Assessments.FromSqlInterpolated(
                    $"SELECT * FROM Assessments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}")
                    .SingleOrDefaultAsync(cancellationToken);
                if (assessment == null) return Failure(courseId, "Không tìm thấy Assessment hợp lệ.");
                await _db.Entry(assessment).ReloadAsync(cancellationToken);
                if (assessment.OwnerTeacherUserId != actorUserId || assessment.CourseId != courseId)
                    return Failure(courseId, "Liên kết Assessment đã thay đổi. Hãy tải lại trang.");
                locked.Add(id, assessment);
            }

            if (course.FinalAssessmentId == request.FinalAssessmentId)
            {
                await tx.CommitAsync(cancellationToken);
                return WriteOutcome.Ok(courseId);
            }

            if (course.FinalAssessmentId.HasValue && await _db.Attempts.AnyAsync(
                a => a.AssessmentId == course.FinalAssessmentId.Value, cancellationToken))
            {
                // Không dựa vào Enrollment hiện tại: Enrollment có thể đã bị thu hồi.
                // Attempt của đề gắn Course là bằng chứng Final đã được sử dụng.
                return Failure(courseId, "Final đã có Attempt. Không được thay/gỡ, kể cả bằng bản clone.");
            }

            if (request.FinalAssessmentId.HasValue)
            {
                Assessment candidate = locked[request.FinalAssessmentId.Value];
                if (candidate.Status != AssessmentStatus.Published)
                    return Failure(courseId, "Final phải là Assessment đang Published.");
                if (candidate.AssessmentType != AssessmentType.SkillAssessment
                    && candidate.AssessmentType != AssessmentType.PracticeExam)
                    return Failure(courseId, "Final chỉ dùng Skill Assessment hoặc Practice Exam; không dùng Placement Test.");
                if (!candidate.PassingScore.HasValue)
                    return Failure(courseId, "Assessment làm Final phải có PassingScore.");
                if (await _db.Courses.AnyAsync(c => c.Id != courseId && c.FinalAssessmentId == candidate.Id, cancellationToken))
                    return Failure(courseId, "Assessment đang làm Final của Course khác.");

                var questions = await _db.AssessmentQuestions.AsNoTracking()
                    .Where(q => q.AssessmentId == candidate.Id)
                    .Include(q => q.Question).ThenInclude(q => q.Options)
                    .Include(q => q.Question).ThenInclude(q => q.Stimulus)
                    .AsSingleQuery().OrderBy(q => q.OrderIndex).ThenBy(q => q.Id).ToListAsync(cancellationToken);
                // Kiểm tra cả điều kiện Final dự kiến, chưa gán FK vào Course thật.
                var futureCourse = new Course
                {
                    Id = course.Id, OwnerTeacherUserId = actorUserId, Status = course.Status,
                    FinalAssessmentId = candidate.Id
                };
                var errors = _validator.Validate(candidate, questions, course, new[] { futureCourse }, isFirstPublish: false);
                if (errors.Count > 0) return Failure(courseId, errors.ToArray());
                string? audioError = await StimulusAudioIntegrity.GetErrorAsync(
                    questions.Select(q => q.Question.Stimulus), _audioStore, cancellationToken);
                if (audioError != null) return Failure(courseId, audioError);
            }

            var now = DateTimeOffset.UtcNow;
            if (course.UpdatedAtUtc.HasValue && now <= course.UpdatedAtUtc.Value)
                now = course.UpdatedAtUtc.Value.AddTicks(1);
            course.FinalAssessmentId = request.FinalAssessmentId;
            course.UpdatedAtUtc = now;
            await _db.SaveChangesAsync(cancellationToken);

            // Hàm dùng chung chỉ ghi mốc còn null; không xóa CompletedAt lịch sử.
            // Chạy trước commit để tất cả Enrollment được cập nhật cùng liên kết Final.
            await CourseCompletion.ReconcileAsync(_db, courseId, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return WriteOutcome.Ok(courseId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Changing Course Final failed for {CourseId}.", courseId);
            return Failure(courseId, "Chưa thể lưu Final. Hãy tải lại trang và thử lại.");
        }
    }

    // Role trong cookie không thay thế quyền hiện tại trong database.
    private Task<bool> IsTeacherAsync(Guid id, CancellationToken ct) => _db.Users.AsNoTracking().AnyAsync(u =>
        u.Id == id && u.AccountStatus == AccountStatus.Active
        && _db.UserRoles.Count(ur => ur.UserId == id) == 1
        && _db.UserRoles.Any(ur => ur.UserId == id && _db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Teacher)), ct);

    private static WriteOutcome Failure(Guid id, params string[] messages) => new(
        LearningAccessResult.Allowed, id, new Dictionary<string, string[]> { [string.Empty] = messages });
}
