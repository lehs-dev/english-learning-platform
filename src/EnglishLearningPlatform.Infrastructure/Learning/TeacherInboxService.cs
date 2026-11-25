using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

public sealed class TeacherInboxService(AppDbContext db, IActiveUserRoleReader users) : ITeacherInboxService
{
    private const int PreviewSize = 5;
    private const int PageSize = 20;

    public async Task<LearningOutcome<TeacherInboxPage>> GetAsync(Guid teacherId, TeacherInboxQuery query, CancellationToken ct)
    {
        if (await users.GetActiveRoleAsync(teacherId, ct) != AppRoles.Teacher)
            return new(LearningAccessResult.Forbidden);

        var courses = db.Courses.AsNoTracking().Where(course => course.OwnerTeacherUserId == teacherId &&
            (course.Status == CourseStatus.Published || course.Status == CourseStatus.Unpublished));
        var options = await courses.OrderBy(course => course.Title).ThenBy(course => course.Id)
            .Select(course => new TeacherInboxCourse(course.Id, course.Title)).ToListAsync(ct);
        if (query.CourseId is Guid selectedCourse)
        {
            if (!options.Any(course => course.Id == selectedCourse)) return new(LearningAccessResult.NotFound);
            courses = courses.Where(course => course.Id == selectedCourse);
        }

        var writing = db.WritingSubmissions.AsNoTracking()
            .Where(row => row.ReviewedAtUtc == null && courses.Any(course => course.Id == row.Assignment.CourseId))
            .GroupBy(row => new { row.AssignmentId, row.Assignment.Title, CourseTitle = row.Assignment.Course.Title })
            .Select(group => new
            {
                Id = group.Key.AssignmentId,
                group.Key.Title,
                group.Key.CourseTitle,
                WaitingSinceUtc = group.Min(row => row.CreatedAtUtc),
                Pending = group.Count()
            });

        var enrollments = db.Enrollments.AsNoTracking().Valid();
        var discussions =
            from thread in db.CourseDiscussionThreads.AsNoTracking()
            where courses.Any(course => course.Id == thread.Enrollment.CourseId)
                && enrollments.Any(enrollment => enrollment.Id == thread.EnrollmentId)
            from latest in thread.Messages.OrderByDescending(message => message.SequenceNumber).Take(1)
            where !latest.WrittenByTeacher
            select new { thread.Id, thread.Subject, CourseTitle = thread.Enrollment.Course.Title, latest.CreatedAtUtc };

        var writingCount = await writing.CountAsync(ct);
        var discussionCount = await discussions.CountAsync(ct);
        var kind = Enum.IsDefined(query.Kind) ? query.Kind : TeacherInboxKind.All;
        var isOverview = kind == TeacherInboxKind.All;
        var pageSize = isOverview ? PreviewSize : PageSize;
        var selectedCount = kind == TeacherInboxKind.Writing ? writingCount : discussionCount;
        var pages = isOverview ? 1 : Math.Max(1, (int)Math.Ceiling((double)selectedCount / PageSize));
        var page = Math.Clamp(query.Page, 1, pages);
        var offset = (page - 1) * pageSize;

        IReadOnlyList<TeacherInboxItem> writingItems = kind != TeacherInboxKind.Discussion && writingCount > 0
            ? await writing.OrderBy(item => item.WaitingSinceUtc).ThenBy(item => item.Id).Skip(offset).Take(pageSize)
                .Select(item => new TeacherInboxItem(item.Id, item.Title, item.CourseTitle, item.WaitingSinceUtc, item.Pending))
                .ToListAsync(ct) : [];
        IReadOnlyList<TeacherInboxItem> discussionItems = kind != TeacherInboxKind.Writing && discussionCount > 0
            ? await discussions.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).Skip(offset).Take(pageSize)
                .Select(item => new TeacherInboxItem(item.Id, item.Subject, item.CourseTitle, item.CreatedAtUtc, 1))
                .ToListAsync(ct) : [];
        return new(LearningAccessResult.Allowed,
            new(query with { Kind = kind, Page = page }, options, writingItems, discussionItems, writingCount, discussionCount, pages));
    }
}
