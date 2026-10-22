using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

// Tránh nhầm tên entity Practice với namespace Application.Practice.
using PracticeEntity = EnglishLearningPlatform.Domain.Entities.Practice;

namespace EnglishLearningPlatform.Infrastructure.Practices;

/// <summary>
/// Đọc danh sách Practice dành cho Teacher.
/// Service này không tạo hoặc cập nhật dữ liệu.
/// </summary>
public sealed class TeacherPracticeQueryService
    : ITeacherPracticeQueryService
{
    private readonly AppDbContext _db;

    public TeacherPracticeQueryService(AppDbContext db)
    {
        if (db == null)
        {
            throw new ArgumentNullException(nameof(db));
        }

        _db = db;
    }

    /// <summary>
    /// Kiểm tra quyền bằng dữ liệu hiện tại trong database.
    /// Giữ cùng nguyên tắc với service tạo Draft:
    /// tài khoản Active, đúng một role và role đó là Teacher.
    /// </summary>
    private Task<bool> IsActiveTeacherAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        _db.HasActiveRoleAsync(userId, AppRoles.Teacher, cancellationToken);

    /// <summary>
    /// Tìm, lọc và phân trang Practice thuộc Teacher đang đăng nhập.
    /// </summary>
    public async Task<LearningOutcome<TeacherPracticeSearchResult>>
        SearchAsync(
            Guid actorUserId,
            TeacherPracticeSearchRequest request,
            CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // Dừng trước khi truy vấn nếu phía gọi đã hủy request.
        cancellationToken.ThrowIfCancellationRequested();

        // Kiểm tra quyền trước khi xử lý bộ lọc hoặc đọc Practice.
        bool isTeacher = await IsActiveTeacherAsync(
            actorUserId,
            cancellationToken);

        if (!isTeacher)
        {
            return new LearningOutcome<TeacherPracticeSearchResult>(
                LearningAccessResult.Forbidden);
        }

        // Kiểm tra dữ liệu phân trang do client gửi lên.
        // Không thay đổi trực tiếp các giá trị trong request.
        int pageNumber = request.PageNumber;
        int pageSize = request.PageSize;

        if (pageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.PageNumber),
                "Số trang phải bắt đầu từ 1.");
        }

        // Giới hạn giống chức năng tìm kiếm Question Bank hiện tại.
        // Không cho client yêu cầu một trang có quá nhiều bản ghi.
        if (pageSize < 1 || pageSize > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.PageSize),
                "Số Practice mỗi trang phải từ 1 đến 100.");
        }

        if (request.Status.HasValue)
        {
            bool validStatus = Enum.IsDefined(
                typeof(PracticeStatus),
                request.Status.Value);

            if (!validStatus)
            {
                throw new ArgumentException(
                    "Trạng thái Practice không hợp lệ.",
                    nameof(request.Status));
            }
        }

        // Null nghĩa là không lọc Course.
        // Guid.Empty không phải ID Course hợp lệ.
        if (request.CourseId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID Course không hợp lệ.",
                nameof(request.CourseId));
        }

        // Luôn giới hạn owner ngay từ truy vấn gốc.
        // AsNoTracking phù hợp vì chức năng này chỉ đọc dữ liệu.
        IQueryable<PracticeEntity> query = _db.Practices
            .AsNoTracking()
            .Where(practice =>
                practice.OwnerTeacherUserId == actorUserId);

        // Draft chưa có parent vẫn phải xuất hiện.
        // Nếu đã có parent, Course chứa parent phải còn cùng owner.
        // Không trả thông tin Course của Teacher khác.
        query = query.Where(practice =>
            (!practice.ModuleId.HasValue
                && !practice.LessonId.HasValue)
            || (practice.Module != null
                && practice.Module.Course.OwnerTeacherUserId
                    == actorUserId)
            || (practice.Lesson != null
                && practice.Lesson.Module.Course.OwnerTeacherUserId
                    == actorUserId));

        // Bỏ qua tìm kiếm nếu từ khóa null, rỗng hoặc toàn khoảng trắng.
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            string search = request.Search.Trim();

            query = query.Where(practice =>
                practice.Title.Contains(search));
        }

        if (request.Status.HasValue)
        {
            PracticeStatus status = request.Status.Value;

            query = query.Where(practice =>
                practice.Status == status);
        }

        if (request.CourseId.HasValue)
        {
            Guid courseId = request.CourseId.Value;

            // Practice không có CourseId trực tiếp.
            // Phải tìm Course thông qua Module hoặc Lesson.Module.
            query = query.Where(practice =>
                (practice.Module != null
                    && practice.Module.CourseId == courseId)
                || (practice.Lesson != null
                    && practice.Lesson.Module.CourseId == courseId));
        }

        // Đếm sau khi áp dụng owner và tất cả bộ lọc,
        // nhưng trước khi lấy một trang.
        int totalCount = await query.CountAsync(cancellationToken);

        // Dùng long để tránh tràn số khi client gửi số trang rất lớn.
        long skipCount = ((long)pageNumber - 1) * pageSize;

        // Không có dữ liệu hoặc trang vượt quá kết quả:
        // trả danh sách rỗng, không coi là lỗi quyền truy cập.
        if (skipCount >= totalCount)
        {
            var emptyResult = new TeacherPracticeSearchResult(
                new List<TeacherPracticeListItem>(),
                totalCount,
                pageNumber,
                pageSize);

            return new LearningOutcome<TeacherPracticeSearchResult>(
                LearningAccessResult.Allowed,
                emptyResult);
        }

        List<TeacherPracticeListItem> items = await query
            .OrderByDescending(practice => practice.CreatedAtUtc)
            .ThenBy(practice => practice.Id)
            .Skip((int)skipCount)
            .Take(pageSize)
            .Select(practice => new TeacherPracticeListItem
            {
                Id = practice.Id,
                Title = practice.Title,
                Status = practice.Status,
                QuestionCount = practice.Questions.Count,
                CreatedAtUtc = practice.CreatedAtUtc,

                // Giữ nguyên parent trực tiếp của Practice.
                ModuleId = practice.ModuleId,
                LessonId = practice.LessonId,

                ModuleTitle = practice.Module != null
                    ? practice.Module.Title
                    : null,

                LessonTitle = practice.Lesson != null
                    ? practice.Lesson.Title
                    : null,

                // Draft không có parent thì CourseId/Title là null.
                CourseId = practice.Module != null
                    ? (Guid?)practice.Module.CourseId
                    : practice.Lesson != null
                        ? (Guid?)practice.Lesson.Module.CourseId
                        : null,

                CourseTitle = practice.Module != null
                    ? practice.Module.Course.Title
                    : practice.Lesson != null
                        ? practice.Lesson.Module.Course.Title
                        : null
            })
            .ToListAsync(cancellationToken);

        var result = new TeacherPracticeSearchResult(
            items,
            totalCount,
            pageNumber,
            pageSize);

        return new LearningOutcome<TeacherPracticeSearchResult>(
            LearningAccessResult.Allowed,
            result);
    }
}