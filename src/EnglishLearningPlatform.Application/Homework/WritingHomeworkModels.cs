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
    int? AwaitingReview, bool Submitted, bool Reviewed, bool Open)
{
    public bool IsDraft { get; init; }
    public bool Scheduled { get; init; }
    public DateTimeOffset? PublishAtUtc { get; init; }
    public bool CanEdit { get; init; }
}

public sealed record WritingHomeworkCourse(Guid CourseId, string CourseTitle, bool CanAssign,
    IReadOnlyList<WritingHomeworkItem> Assignments, int Page, bool HasNextPage);

public sealed record WritingSubmissionItem(Guid Id, string? StudentName, string Response,
    DateTimeOffset SubmittedAtUtc, string? Feedback, int? Score, DateTimeOffset? ReviewedAtUtc);

public sealed record WritingHomeworkDetail(Guid Id, Guid CourseId, string CourseTitle, string Title,
    string Prompt, DateTimeOffset? DueAtUtc, bool IsTeacher, bool CanSubmit,
    IReadOnlyList<WritingSubmissionItem> Submissions, int Page, bool HasNextPage)
{
    public WritingDraft? Draft { get; init; }
    public bool CanReview { get; init; }
    public bool IsDraft { get; init; }
    public bool Scheduled { get; init; }
    public DateTimeOffset? PublishAtUtc { get; init; }
    public bool CanEdit { get; init; }
}

public sealed record WritingDraft(string Response, DateTimeOffset SavedAtUtc, Guid Version);

public sealed record WritingHomeworkCourseInfo(Guid Id, string Title, bool CanAssign);

public sealed record WritingAssignmentPlan(string? Title, string? Prompt, DateTimeOffset? DueAtUtc, bool IsDraft,
    DateTimeOffset? PublishAtUtc);

public enum WritingHomeworkWriteStatus
{
    Saved, Forbidden, NotFound, Invalid, Closed, AlreadySubmitted, AlreadyReviewed, DraftChanged, AlreadyPublished
}

public sealed record WritingHomeworkWriteResult(WritingHomeworkWriteStatus Status, Guid? AssignmentId = null);

public interface IWritingHomeworkService
{
    Task<LearningOutcome<WritingHomeworkCourse>> GetCourseAsync(Guid actorId, Guid courseId, int page, CancellationToken ct);
    Task<LearningOutcome<WritingHomeworkCourseInfo>> GetAuthoringCourseAsync(Guid teacherId, Guid courseId, CancellationToken ct);
    Task<LearningOutcome<WritingHomeworkDetail>> GetAssignmentAsync(Guid actorId, Guid assignmentId, int page, CancellationToken ct);
    Task<WritingHomeworkWriteResult> AssignAsync(Guid teacherId, Guid courseId, WritingAssignmentPlan plan, CancellationToken ct);
    Task<WritingHomeworkWriteResult> UpdateAssignmentAsync(Guid teacherId, Guid assignmentId, WritingAssignmentPlan plan, CancellationToken ct);
    Task<WritingHomeworkWriteResult> SaveDraftAsync(Guid studentId, Guid assignmentId, string? response,
        Guid? expectedVersion, CancellationToken ct);
    Task<WritingHomeworkWriteResult> SubmitAsync(Guid studentId, Guid assignmentId, string? response, CancellationToken ct);
    Task<WritingHomeworkWriteResult> ReviewAsync(Guid teacherId, Guid assignmentId, Guid submissionId,
        string? feedback, int? score, CancellationToken ct);
}
