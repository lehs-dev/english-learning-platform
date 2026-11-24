namespace EnglishLearningPlatform.Application.Learning;

public static class CourseDiscussionLimits
{
    public const int SubjectLength = 160;
    public const int BodyLength = 4000;
}

public sealed record CourseDiscussionHeader(Guid CourseId, string CourseTitle, bool IsTeacher, bool CanWrite);

public sealed record CourseDiscussionThreadSummary(Guid Id, string Subject, string ParticipantName,
    DateTimeOffset LastMessageAtUtc, bool IsAwaitingTeacher);

public sealed record CourseDiscussionMessageItem(string Body, DateTimeOffset SentAtUtc, bool WrittenByTeacher, bool IsMine);

public sealed record CourseDiscussionInbox(CourseDiscussionHeader Course, int Page, int Pages,
    IReadOnlyList<CourseDiscussionThreadSummary> Threads);

public sealed record CourseDiscussionConversation(CourseDiscussionHeader Course, Guid ThreadId, string Subject,
    string ParticipantName, int Page, int Pages, IReadOnlyList<CourseDiscussionMessageItem> Messages);

public interface ICourseDiscussionService
{
    Task<LearningOutcome<CourseDiscussionInbox>> GetInboxAsync(Guid userId, Guid courseId, int page, CancellationToken ct = default);
    Task<LearningOutcome<CourseDiscussionConversation>> GetConversationAsync(Guid userId, Guid threadId, int page, CancellationToken ct = default);
    Task<WriteOutcome> AskAsync(Guid studentId, Guid courseId, Guid requestId, string? subject, string? body, CancellationToken ct = default);
    Task<WriteOutcome> ReplyAsync(Guid userId, Guid threadId, Guid requestId, string? body, CancellationToken ct = default);
}
