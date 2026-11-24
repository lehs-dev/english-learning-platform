using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Application.Homework;

public static class WritingHomeworkLimits
{
    public const int TitleLength = 160;
    public const int PromptLength = 4000;
    public const int ResponseLength = 6000;
    public const int FeedbackLength = 4000;
    public const int PageSize = 20;
    public static readonly TimeSpan DisplayOffset = TimeSpan.FromHours(7);
}

public sealed record WritingHomeworkItem(Guid Id, string Title, DateTimeOffset? DueAtUtc,
    int? AwaitingReview, bool Submitted, bool Reviewed, bool Open);

public sealed record WritingHomeworkCourse(Guid CourseId, string CourseTitle, bool CanAssign,
    IReadOnlyList<WritingHomeworkItem> Assignments, int Page, bool HasNextPage);

public sealed record WritingSubmissionItem(Guid Id, string? StudentName, string Response,
    DateTimeOffset SubmittedAtUtc, string? Feedback, int? Score, DateTimeOffset? ReviewedAtUtc);

public sealed record WritingHomeworkDetail(Guid Id, Guid CourseId, string CourseTitle, string Title,
    string Prompt, DateTimeOffset? DueAtUtc, bool IsTeacher, bool CanSubmit,
    IReadOnlyList<WritingSubmissionItem> Submissions, int Page, bool HasNextPage);

public sealed record WritingHomeworkCourseInfo(Guid Id, string Title, bool CanAssign);

public enum WritingHomeworkWriteStatus { Saved, Forbidden, NotFound, Invalid, Closed, AlreadySubmitted, AlreadyReviewed }

public sealed record WritingHomeworkWriteResult(WritingHomeworkWriteStatus Status, Guid? AssignmentId = null);

public interface IWritingHomeworkService
{
    Task<LearningOutcome<WritingHomeworkCourse>> GetCourseAsync(Guid actorId, Guid courseId, int page, CancellationToken ct);
    Task<LearningOutcome<WritingHomeworkCourseInfo>> GetAuthoringCourseAsync(Guid teacherId, Guid courseId, CancellationToken ct);
    Task<LearningOutcome<WritingHomeworkDetail>> GetAssignmentAsync(Guid actorId, Guid assignmentId, int page, CancellationToken ct);
    Task<WritingHomeworkWriteResult> AssignAsync(Guid teacherId, Guid courseId, string? title, string? prompt,
        DateTimeOffset? dueAtUtc, CancellationToken ct);
    Task<WritingHomeworkWriteResult> SubmitAsync(Guid studentId, Guid assignmentId, string? response, CancellationToken ct);
    Task<WritingHomeworkWriteResult> ReviewAsync(Guid teacherId, Guid assignmentId, Guid submissionId,
        string? feedback, int? score, CancellationToken ct);
}
