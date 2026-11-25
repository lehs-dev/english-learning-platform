namespace EnglishLearningPlatform.Application.Learning;

public enum TeacherInboxKind { All, Writing, Discussion }

public sealed record TeacherInboxQuery
{
    public Guid? CourseId { get; init; }
    public TeacherInboxKind Kind { get; init; }
    public int Page { get; init; } = 1;
}

public sealed record TeacherInboxCourse(Guid Id, string Title);

public sealed record TeacherInboxItem(Guid Id, string Title, string CourseTitle, DateTimeOffset WaitingSinceUtc, int Pending);

public sealed record TeacherInboxPage(TeacherInboxQuery Query, IReadOnlyList<TeacherInboxCourse> Courses,
    IReadOnlyList<TeacherInboxItem> Writing, IReadOnlyList<TeacherInboxItem> Discussions, int WritingCount, int DiscussionCount,
    int PageCount);

public interface ITeacherInboxService
{
    Task<LearningOutcome<TeacherInboxPage>> GetAsync(Guid teacherId, TeacherInboxQuery query, CancellationToken ct);
}
