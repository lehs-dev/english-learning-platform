using System.Data;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

// Đặt tên khác cho entity để tránh nhầm với namespace Application.Practice.
using PracticeEntity = EnglishLearningPlatform.Domain.Entities.Practice;

namespace EnglishLearningPlatform.Infrastructure.Practices;

public sealed class TeacherPracticeService : ITeacherPracticeService
{
    private readonly AppDbContext _db;
    private readonly ICreatePracticeValidator _validator;

    public TeacherPracticeService(
        AppDbContext db,
        ICreatePracticeValidator validator)
    {
        // Các dependency này phải được DI cung cấp.
        if (db is null)
        {
            throw new ArgumentNullException(nameof(db));
        }

        if (validator is null)
        {
            throw new ArgumentNullException(nameof(validator));
        }

        _db = db;
        _validator = validator;
    }

    public async Task<WriteOutcome> CreateDraftAsync(
        Guid actorUserId,
        CreatePracticeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 1. Kiểm tra quyền trước khi xử lý dữ liệu.
        if (!await IsActiveTeacherAsync(
            actorUserId,
            cancellationToken))
        {
            return WriteOutcome.Denied(
                LearningAccessResult.Forbidden);
        }

        // 2. Kiểm tra tên, mô tả và quy tắc chọn parent.
        var errors = _validator.Validate(request);

        if (errors.Count > 0)
        {
            // Có quyền thực hiện, nhưng dữ liệu nhập chưa hợp lệ.
            // Guid.Empty nghĩa là chưa tạo Practice nào.
            return new WriteOutcome(
                LearningAccessResult.Allowed,
                Guid.Empty,
                errors);
        }

        // 3. Xác định Course chứa parent nếu Teacher đã chọn parent.
        bool hasParent =
            request.LessonId.HasValue || request.ModuleId.HasValue;

        Guid? courseId = null;

        string parentField = request.LessonId.HasValue
            ? nameof(CreatePracticeRequest.LessonId)
            : nameof(CreatePracticeRequest.ModuleId);

        if (hasParent)
        {
            courseId = await GetParentCourseIdAsync(
                actorUserId,
                request,
                cancellationToken);

            if (!courseId.HasValue)
            {
                return InvalidParent(parentField);
            }
        }

        // 4. Kiểm tra lại và lưu trong cùng một transaction.
        // Nếu thoát trước CommitAsync, transaction sẽ được rollback.
        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        if (courseId.HasValue)
        {
            // Dự án hiện tại dùng thứ tự khóa Course trước.
            // Giữ cùng cách này để phối hợp với các luồng của nhóm.
            // The persistence query passes the course ID as a SQL parameter.
            var course = await _db.CoursesForUpdate(courseId.Value)
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);

            if (course is null
                || course.OwnerTeacherUserId != actorUserId)
            {
                return InvalidParent(parentField);
            }

            // Kiểm tra parent vẫn tồn tại trong đúng Course.
            // Không chỉ tin kết quả đọc trước khi bắt đầu transaction.
            if (request.LessonId.HasValue)
            {
                bool validLesson = await _db.Lessons
                    .AsNoTracking()
                    .AnyAsync(
                        lesson =>
                            lesson.Id == request.LessonId.Value
                            && lesson.Module.CourseId == courseId.Value,
                        cancellationToken);

                if (!validLesson)
                {
                    return InvalidParent(parentField);
                }
            }
            else if (request.ModuleId.HasValue)
            {
                bool validModule = await _db.Modules
                    .AsNoTracking()
                    .AnyAsync(
                        module =>
                            module.Id == request.ModuleId.Value
                            && module.CourseId == courseId.Value,
                        cancellationToken);

                if (!validModule)
                {
                    return InvalidParent(parentField);
                }
            }
        }

        // Đọc lại quyền hiện tại trước khi ghi.
        // Tài khoản có thể đã bị khóa sau lần kiểm tra ban đầu.
        if (!await IsActiveTeacherAsync(
            actorUserId,
            cancellationToken))
        {
            return WriteOutcome.Denied(
                LearningAccessResult.Forbidden);
        }

        // 5. Server tự đặt owner và trạng thái.
        // Client không được quyết định hai giá trị này.
        var practice = new PracticeEntity
        {
            OwnerTeacherUserId = actorUserId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            LessonId = request.LessonId,
            ModuleId = request.ModuleId,
            Status = PracticeStatus.Draft
        };

        // Draft được phép chưa có câu hỏi và chưa chọn parent.
        // Các điều kiện bắt buộc khi Publish sẽ xử lý riêng.
        _db.Practices.Add(practice);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return WriteOutcome.Ok(practice.Id);
    }

    /// <summary>
    /// Kiểm tra quyền bằng dữ liệu hiện tại trong database.
    /// Không dựa vào role hoặc ID do form gửi lên.
    /// </summary>
    private Task<bool> IsActiveTeacherAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        _db.HasActiveRoleAsync(userId, AppRoles.Teacher, cancellationToken);

    /// <summary>
    /// Tìm Course chứa parent, nhưng chỉ trong phạm vi Teacher sở hữu.
    /// Lesson và Module không có owner riêng; owner nằm trên Course.
    /// </summary>
    private async Task<Guid?> GetParentCourseIdAsync(
        Guid actorUserId,
        CreatePracticeRequest request,
        CancellationToken cancellationToken)
    {
        if (request.LessonId.HasValue)
        {
            return await _db.Lessons
                .AsNoTracking()
                .Where(
                    lesson =>
                        lesson.Id == request.LessonId.Value
                        && lesson.Module.Course.OwnerTeacherUserId
                            == actorUserId)
                .Select(lesson => (Guid?)lesson.Module.CourseId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (request.ModuleId.HasValue)
        {
            return await _db.Modules
                .AsNoTracking()
                .Where(
                    module =>
                        module.Id == request.ModuleId.Value
                        && module.Course.OwnerTeacherUserId
                            == actorUserId)
                .Select(module => (Guid?)module.CourseId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Trả lỗi chung, không tiết lộ thông tin parent của Teacher khác.
    /// </summary>
    private static WriteOutcome InvalidParent(string field)
    {
        var errors = new Dictionary<string, string[]>();

        errors[field] = new string[]
        {
            "Lesson hoặc Module không tồn tại hoặc không thuộc quyền sở hữu của bạn."
        };

        return new WriteOutcome(
            LearningAccessResult.Allowed,
            Guid.Empty,
            errors);
    }
}
